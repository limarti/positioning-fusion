using Backend.Hubs;
using Backend.Hardware.Gnss;
using Microsoft.AspNetCore.SignalR;
using System.IO.Ports;

namespace Backend.Hardware.Gnss;

public class GnssService : BackgroundService
{
    private readonly IHubContext<DataHub> _hubContext;
    private readonly ILogger<GnssService> _logger;
    private readonly GnssInitializer _gnssInitializer;
    private SerialPort? _serialPort;
    private readonly List<byte> _dataBuffer = new();

    public GnssService(IHubContext<DataHub> hubContext, ILogger<GnssService> logger, GnssInitializer gnssInitializer)
    {
        _hubContext = hubContext;
        _logger = logger;
        _gnssInitializer = gnssInitializer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GNSS Service started");

        _serialPort = _gnssInitializer.GetSerialPort();
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            _logger.LogWarning("GNSS serial port not available - service will not collect data");
            return;
        }

        _logger.LogInformation("GNSS Service connected to {PortName} at {BaudRate} baud",
            _serialPort.PortName, _serialPort.BaudRate);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReadAndProcessGnssDataAsync(stoppingToken);
                await Task.Delay(50, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GNSS data processing");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task ReadAndProcessGnssDataAsync(CancellationToken stoppingToken)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return;

        try
        {
            if (_serialPort.BytesToRead > 0)
            {
                var bytesToRead = Math.Min(_serialPort.BytesToRead, 1024);
                var buffer = new byte[bytesToRead];
                var bytesRead = _serialPort.Read(buffer, 0, bytesToRead);

                _logger.LogDebug("Read {BytesRead} bytes from GNSS", bytesRead);

                for (int i = 0; i < bytesRead; i++)
                {
                    _dataBuffer.Add(buffer[i]);
                }

                await ProcessBufferedDataAsync(stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading GNSS data");
        }
    }

    private async Task ProcessBufferedDataAsync(CancellationToken stoppingToken)
    {
        while (_dataBuffer.Count >= 8)
        {
            var messageStart = FindUbxMessage();
            if (messageStart == -1)
            {
                // No UBX message found - log what we got and look for other protocols
                if (_dataBuffer.Count > 10)
                {
                    var sampleData = string.Join(" ", _dataBuffer.Take(10).Select(b => $"{b:X2}"));
                    var asciiData = string.Join("", _dataBuffer.Take(10).Select(b => b >= 32 && b <= 126 ? (char)b : '.'));
                    _logger.LogDebug("❌ No UBX sync found. First 10 bytes: {SampleData} (ASCII: '{AsciiData}')", sampleData, asciiData);

                    // Log non-UBX data for debugging
                    var bufferString = global::System.Text.Encoding.ASCII.GetString(_dataBuffer.ToArray());
                    if (bufferString.Contains("$"))
                    {
                        _logger.LogDebug("📡 Ignoring NMEA data - UBX configuration in progress");
                    }
                }
                _dataBuffer.Clear();
                break;
            }

            if (messageStart > 0)
            {
                _dataBuffer.RemoveRange(0, messageStart);
                continue;
            }

            if (_dataBuffer.Count < 8)
                break;

            var messageClass = _dataBuffer[2];
            var messageId = _dataBuffer[3];
            var length = (ushort)(_dataBuffer[4] | (_dataBuffer[5] << 8));

            var totalLength = 8 + length;
            if (_dataBuffer.Count < totalLength)
                break;

            var messageData = _dataBuffer.GetRange(6, length).ToArray();
            _dataBuffer.RemoveRange(0, totalLength);

            await ProcessUbxMessage(messageClass, messageId, messageData, stoppingToken);
        }
    }

    private int FindUbxMessage()
    {
        for (int i = 0; i < _dataBuffer.Count - 1; i++)
        {
            if (_dataBuffer[i] == UbxConstants.SYNC_CHAR_1 && _dataBuffer[i + 1] == UbxConstants.SYNC_CHAR_2)
            {
                _logger.LogDebug("Found UBX message at buffer position {Position}", i);
                return i;
            }
        }
        return -1;
    }

    // NMEA fallback removed - UBX binary messages only

    private async Task ProcessUbxMessage(byte messageClass, byte messageId, byte[] data, CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogDebug("Processing UBX message: Class=0x{Class:X2}, ID=0x{Id:X2}, Length={Length}",
                messageClass, messageId, data.Length);

            if (messageClass == UbxConstants.CLASS_NAV && messageId == UbxConstants.NAV_SAT)
            {
                await ProcessNavSatMessage(data, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_NAV && messageId == UbxConstants.NAV_PVT)
            {
                await ProcessNavPvtMessage(data, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_CFG)
            {
                //_logger.LogInformation("📋 Received CFG message: ID=0x{Id:X2}, Length={Length}", messageId, data.Length);
                // Log configuration responses for debugging
            }
            else if (messageClass == UbxConstants.CLASS_MON && messageId == UbxConstants.MON_VER)
            {
                await ProcessMonVerMessage(data, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_RXM && messageId == UbxConstants.RXM_SFRBX)
            {
                await ProcessRxmSfrbxMessage(data, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_RXM && messageId == UbxConstants.RXM_RAWX)
            {
                await ProcessRxmRawxMessage(data, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_ACK)
            {
                if (messageId == UbxConstants.ACK_ACK)
                {
                    _logger.LogInformation("✅ UBX command acknowledged");
                }
                else if (messageId == UbxConstants.ACK_NAK)
                {
                    _logger.LogWarning("❌ UBX command rejected (NAK received)");
                }
            }
            else
            {
                _logger.LogInformation("🔍 Received UBX message Class=0x{Class:X2}, ID=0x{Id:X2}, Length={Length} bytes",
                    messageClass, messageId, data.Length);

                // Log first few bytes for debugging
                if (data.Length > 0)
                {
                    var sampleData = string.Join(" ", data.Take(Math.Min(data.Length, 16)).Select(b => $"{b:X2}"));
                    _logger.LogDebug("UBX message data (first 16 bytes): {SampleData}", sampleData);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing UBX message Class=0x{Class:X2}, ID=0x{Id:X2}", messageClass, messageId);
        }
    }

    private async Task ProcessNavPosllhMessage(byte[] data, CancellationToken stoppingToken)
    {
        _logger.LogDebug("ProcessNavPosllhMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 28)
        {
            _logger.LogWarning("NAV-POSLLH message too short: {Length} bytes, minimum 28 required", data.Length);
            return;
        }

        var iTow = BitConverter.ToUInt32(data, 0);
        var lon = BitConverter.ToInt32(data, 4) * 1e-7;
        var lat = BitConverter.ToInt32(data, 8) * 1e-7;
        var height = BitConverter.ToInt32(data, 12) / 1000.0; // mm to m
        var hMSL = BitConverter.ToInt32(data, 16) / 1000.0; // mm to m
        var hAcc = BitConverter.ToUInt32(data, 20) / 1000.0; // mm to m
        var vAcc = BitConverter.ToUInt32(data, 24) / 1000.0; // mm to m

        _logger.LogInformation("NAV-POSLLH: iTow={iTow}, Lat={Lat:F7}°, Lon={Lon:F7}°, Alt={Alt:F1}m, hAcc={HAcc:F3}m",
            iTow, lat, lon, hMSL, hAcc);

        var positionData = new
        {
            iTow = iTow,
            latitude = lat,
            longitude = lon,
            heightEllipsoid = height,
            heightMSL = hMSL,
            horizontalAccuracy = hAcc,
            verticalAccuracy = vAcc
        };

        try
        {
            await _hubContext.Clients.All.SendAsync("PositionUpdate", positionData, stoppingToken);
            _logger.LogInformation("✅ Position data sent to frontend: Lat={Lat:F7}, Lon={Lon:F7}", lat, lon);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send position data to frontend");
        }
    }

    private async Task ProcessNavStatusMessage(byte[] data, CancellationToken stoppingToken)
    {
        _logger.LogDebug("ProcessNavStatusMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 16)
        {
            _logger.LogWarning("NAV-STATUS message too short: {Length} bytes, minimum 16 required", data.Length);
            return;
        }

        var iTow = BitConverter.ToUInt32(data, 0);
        var gpsFix = data[4];
        var flags = data[5];
        var diffStat = data[6];
        var ttff = BitConverter.ToUInt32(data, 8);
        var msss = BitConverter.ToUInt32(data, 12);

        var gpsFixOk = (flags & 0x01) != 0;
        var diffSoln = (flags & 0x02) != 0;

        var fixTypeString = gpsFix switch
        {
            0x00 => "No Fix",
            0x01 => "Dead Reckoning",
            0x02 => "2D Fix",
            0x03 => "3D Fix",
            0x04 => "GPS+DR",
            0x05 => "Time Only",
            _ => $"Unknown({gpsFix})"
        };

        _logger.LogInformation("NAV-STATUS: iTow={iTow}, Fix={FixType}, GpsFixOk={GpsFixOk}, DiffSoln={DiffSoln}, TTFF={Ttff}ms",
            iTow, fixTypeString, gpsFixOk, diffSoln, ttff);

        var statusData = new
        {
            iTow = iTow,
            fixType = gpsFix,
            fixTypeString = fixTypeString,
            gpsFixOk = gpsFixOk,
            differentialSolution = diffSoln,
            timeToFirstFix = ttff
        };

        try
        {
            await _hubContext.Clients.All.SendAsync("StatusUpdate", statusData, stoppingToken);
            _logger.LogInformation("✅ Status data sent to frontend: {FixType}", fixTypeString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send status data to frontend");
        }
    }

    private async Task ProcessNavSolMessage(byte[] data, CancellationToken stoppingToken)
    {
        _logger.LogDebug("ProcessNavSolMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 52)
        {
            _logger.LogWarning("NAV-SOL message too short: {Length} bytes, minimum 52 required", data.Length);
            return;
        }

        var iTow = BitConverter.ToUInt32(data, 0);
        var fTow = BitConverter.ToInt32(data, 4);
        var week = BitConverter.ToInt16(data, 8);
        var gpsFix = data[10];
        var flags = data[11];
        var ecefX = BitConverter.ToInt32(data, 12);
        var ecefY = BitConverter.ToInt32(data, 16);
        var ecefZ = BitConverter.ToInt32(data, 20);
        var pAcc = BitConverter.ToUInt32(data, 24);
        var ecefVX = BitConverter.ToInt32(data, 28);
        var ecefVY = BitConverter.ToInt32(data, 32);
        var ecefVZ = BitConverter.ToInt32(data, 36);
        var sAcc = BitConverter.ToUInt32(data, 40);
        var pDOP = BitConverter.ToUInt16(data, 44);
        var numSV = data[47];

        var gpsFixOk = (flags & 0x01) != 0;
        var diffSoln = (flags & 0x02) != 0;

        _logger.LogInformation("NAV-SOL: iTow={iTow}, Week={Week}, Fix={FixType}, NumSV={NumSV}, PDOP={Pdop}, pAcc={PAcc}mm",
            iTow, week, gpsFix, numSV, pDOP * 0.01, pAcc);

        var solutionData = new
        {
            iTow = iTow,
            week = week,
            fixType = gpsFix,
            gpsFixOk = gpsFixOk,
            differentialSolution = diffSoln,
            numSatellites = numSV,
            pdop = pDOP * 0.01,
            positionAccuracy = pAcc,
            speedAccuracy = sAcc
        };

        try
        {
            await _hubContext.Clients.All.SendAsync("SolutionUpdate", solutionData, stoppingToken);
            _logger.LogInformation("✅ Solution data sent to frontend: Fix={FixType}, {NumSV} sats", gpsFix, numSV);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send solution data to frontend");
        }
    }

    private async Task ProcessNavSatMessage(byte[] data, CancellationToken stoppingToken)
    {
        _logger.LogDebug("ProcessNavSatMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 8)
        {
            _logger.LogWarning("NAV-SAT message too short: {Length} bytes, minimum 8 required", data.Length);
            return;
        }

        var iTow = BitConverter.ToUInt32(data, 0);
        var version = data[4];
        var numSvs = data[5];

        _logger.LogInformation("NAV-SAT: iTow={iTow}, version={Version}, numSvs={NumSvs}, dataLength={DataLength}",
            iTow, version, numSvs, data.Length);

        // Check if we have enough data for all satellites
        var expectedLength = 8 + (numSvs * 12);
        if (data.Length < expectedLength)
        {
            _logger.LogWarning("NAV-SAT message incomplete: expected {Expected} bytes, got {Actual} bytes for {NumSvs} satellites",
                expectedLength, data.Length, numSvs);
            return;
        }

        var satellites = new List<object>();
        var constellationCounts = new Dictionary<string, int>();

        for (int i = 0; i < numSvs && (8 + i * 12) + 11 < data.Length; i++)
        {
            var offset = 8 + i * 12;

            var gnssId = data[offset];
            var svId = data[offset + 1];
            var cno = data[offset + 2];
            var elev = (sbyte)data[offset + 3];
            var azim = BitConverter.ToInt16(data, offset + 4);
            var prRes = BitConverter.ToInt16(data, offset + 6);
            var flags = BitConverter.ToUInt32(data, offset + 8);

            var qualityInd = (flags >> 0) & 0x7;
            var svUsed = (flags & 0x8) != 0;
            var health = (flags >> 4) & 0x3;
            var diffCorr = (flags & 0x40) != 0;
            var smoothed = (flags & 0x80) != 0;

            var gnssName = GetGnssName(gnssId);

            // Count satellites by constellation
            constellationCounts[gnssName] = constellationCounts.GetValueOrDefault(gnssName, 0) + 1;

            _logger.LogDebug("Satellite {Index}: {Constellation} {SvId}, C/N0={Cno}, Elev={Elev}°, Az={Az}°, Used={Used}",
                i, gnssName, svId, cno, elev, azim, svUsed);

            satellites.Add(new
            {
                gnssId = gnssId,
                gnssName = gnssName,
                svId = svId,
                cno = cno,
                elevation = elev,
                azimuth = azim,
                pseudorangeResidual = prRes * 0.1, // Convert to meters
                qualityIndicator = qualityInd,
                svUsed = svUsed,
                health = health,
                differentialCorrection = diffCorr,
                smoothed = smoothed
            });
        }

        // Log constellation summary
        var constellationSummary = string.Join(", ",
            constellationCounts.Select(kv => $"{kv.Key}:{kv.Value}"));
        _logger.LogInformation("Parsed {TotalSats} satellites: {ConstellationSummary}",
            satellites.Count, constellationSummary);

        var satelliteData = new
        {
            iTow = iTow,
            numSatellites = numSvs,
            satellites = satellites
        };

        try
        {
            await _hubContext.Clients.All.SendAsync("SatelliteUpdate", satelliteData, stoppingToken);
            //_logger.LogInformation("✅ Satellite data sent to frontend: {NumSats} satellites", numSvs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send satellite data to frontend");
        }
    }

    private async Task ProcessNavPvtMessage(byte[] data, CancellationToken stoppingToken)
    {
        _logger.LogDebug("ProcessNavPvtMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 84)
        {
            _logger.LogWarning("NAV-PVT message too short: {Length} bytes, minimum 84 required", data.Length);
            return;
        }

        var iTow = BitConverter.ToUInt32(data, 0);
        var year = BitConverter.ToUInt16(data, 4);
        var month = data[6];
        var day = data[7];
        var hour = data[8];
        var min = data[9];
        var sec = data[10];
        var valid = data[11];
        var tAcc = BitConverter.ToUInt32(data, 12);
        var nano = BitConverter.ToInt32(data, 16);
        var fixType = data[20];
        var flags = data[21];
        var flags2 = data[22];
        var numSV = data[23];
        var lon = BitConverter.ToInt32(data, 24) * 1e-7;
        var lat = BitConverter.ToInt32(data, 28) * 1e-7;
        var height = BitConverter.ToInt32(data, 32);
        var hMSL = BitConverter.ToInt32(data, 36);
        var hAcc = BitConverter.ToUInt32(data, 40);
        var vAcc = BitConverter.ToUInt32(data, 44);

        var gnssFixOk = (flags & 0x01) != 0;
        var diffSoln = (flags & 0x02) != 0;
        var psmState = (flags >> 2) & 0x07;
        var headVehValid = (flags & 0x20) != 0;
        var carrSoln = (flags >> 6) & 0x03;

        // Get human-readable fix type
        var fixTypeString = fixType switch
        {
            UbxConstants.FIX_TYPE_NO_FIX => "No Fix",
            UbxConstants.FIX_TYPE_DEAD_RECKONING => "Dead Reckoning",
            UbxConstants.FIX_TYPE_2D => "2D Fix",
            UbxConstants.FIX_TYPE_3D => "3D Fix",
            UbxConstants.FIX_TYPE_GNSS_DR => "GNSS+DR",
            UbxConstants.FIX_TYPE_TIME_ONLY => "Time Only",
            _ => $"Unknown({fixType})"
        };

        var carrierString = carrSoln switch
        {
            UbxConstants.CARRIER_SOLUTION_NONE => "None",
            UbxConstants.CARRIER_SOLUTION_FLOAT => "Float",
            UbxConstants.CARRIER_SOLUTION_FIXED => "Fixed",
            _ => $"Unknown({carrSoln})"
        };

        _logger.LogInformation("NAV-PVT: iTow={iTow}, {DateTime}, Fix={FixType}({FixCode}), Carrier={Carrier}, Sats={NumSV}",
            iTow, $"{year:D4}-{month:D2}-{day:D2} {hour:D2}:{min:D2}:{sec:D2}",
            fixTypeString, fixType, carrierString, numSV);

        _logger.LogInformation("Position: Lat={Lat:F7}°, Lon={Lon:F7}°, Alt={Alt:F1}m, hAcc={HAcc}mm, vAcc={VAcc}mm",
            lat, lon, hMSL / 1000.0, hAcc, vAcc);

        _logger.LogDebug("Flags: gnssFixOk={GnssFixOk}, diffSoln={DiffSoln}, timeValid=0x{TimeValid:X2}",
            gnssFixOk, diffSoln, valid);

        var pvtData = new
        {
            iTow = iTow,
            year = year,
            month = month,
            day = day,
            hour = hour,
            minute = min,
            second = sec,
            timeValid = valid,
            timeAccuracy = tAcc,
            fixType = fixType,
            gnssFixOk = gnssFixOk,
            differentialSolution = diffSoln,
            numSatellites = numSV,
            longitude = lon,
            latitude = lat,
            heightEllipsoid = height,
            heightMSL = hMSL,
            horizontalAccuracy = hAcc,
            verticalAccuracy = vAcc,
            carrierSolution = carrSoln
        };

        try
        {
            await _hubContext.Clients.All.SendAsync("PvtUpdate", pvtData, stoppingToken);
            _logger.LogInformation("✅ PVT data sent to frontend: {FixType}, {NumSV} sats, Lat={Lat:F7}, Lon={Lon:F7}",
                fixTypeString, numSV, lat, lon);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send PVT data to frontend");
        }
    }

    private async Task ProcessRxmSfrbxMessage(byte[] data, CancellationToken stoppingToken)
    {
        _logger.LogDebug("ProcessRxmSfrbxMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 8)
        {
            _logger.LogWarning("RXM-SFRBX message too short: {Length} bytes, minimum 8 required", data.Length);
            return;
        }

        var gnssId = data[0];
        var svId = data[1];
        var freqId = data[2];
        var numWords = data[3];
        var chn = data[4];
        var version = data[5];

        var gnssName = GetGnssName(gnssId);

        //_logger.LogInformation("RXM-SFRBX: {Constellation} SV{SvId}, FreqId={FreqId}, Words={NumWords}, Channel={Channel}, Version={Version}", gnssName, svId, freqId, numWords, chn, version);

        // For broadcast navigation data, we'll extract satellite information
        // Note: This is simplified - full SFRBX parsing would decode the actual navigation message
        var satelliteInfo = new
        {
            gnssId = gnssId,
            gnssName = gnssName,
            svId = svId,
            frequencyId = freqId,
            channel = chn,
            messageLength = data.Length,
            timestamp = DateTime.UtcNow
        };

        try
        {
            await _hubContext.Clients.All.SendAsync("BroadcastDataUpdate", satelliteInfo, stoppingToken);
            //_logger.LogInformation("✅ Broadcast navigation data sent to frontend: {Constellation} SV{SvId}", gnssName, svId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send broadcast data to frontend");
        }
    }

    private async Task ProcessRxmRawxMessage(byte[] data, CancellationToken stoppingToken)
    {
        _logger.LogDebug("ProcessRxmRawxMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 16)
        {
            _logger.LogWarning("RXM-RAWX message too short: {Length} bytes, minimum 16 required", data.Length);
            return;
        }

        var rcvTow = BitConverter.ToDouble(data, 0);
        var week = BitConverter.ToUInt16(data, 8);
        var leapS = (sbyte)data[10];
        var numMeas = data[11];
        var recStat = data[12];
        var version = data[13];

        //_logger.LogInformation("RXM-RAWX: Week={Week}, TOW={Tow:F3}, Measurements={NumMeas}, RecStat=0x{RecStat:X2}, Version={Version}", week, rcvTow, numMeas, recStat, version);

        // Check if we have enough data for all measurements
        var expectedLength = 16 + (numMeas * 32);
        if (data.Length < expectedLength)
        {
            _logger.LogWarning("RXM-RAWX message incomplete: expected {Expected} bytes, got {Actual} bytes for {NumMeas} measurements",
                expectedLength, data.Length, numMeas);
            return;
        }

        var satellites = new List<object>();
        var constellationCounts = new Dictionary<string, int>();

        for (int i = 0; i < numMeas && (16 + i * 32) + 31 < data.Length; i++)
        {
            var offset = 16 + i * 32;

            var prMes = BitConverter.ToDouble(data, offset);
            var cpMes = BitConverter.ToDouble(data, offset + 8);
            var doMes = BitConverter.ToSingle(data, offset + 16);
            var gnssId = data[offset + 20];
            var svId = data[offset + 21];
            var freqId = data[offset + 22];
            var locktime = BitConverter.ToUInt16(data, offset + 23);
            var cno = data[offset + 25];
            var prStdev = data[offset + 26] & 0x0F;
            var cpStdev = data[offset + 27] & 0x0F;
            var doStdev = data[offset + 28] & 0x0F;
            var trkStat = data[offset + 29];

            var gnssName = GetGnssName(gnssId);
            constellationCounts[gnssName] = constellationCounts.GetValueOrDefault(gnssName, 0) + 1;

            var prValid = (trkStat & 0x01) != 0;
            var cpValid = (trkStat & 0x02) != 0;
            var halfCyc = (trkStat & 0x04) != 0;

            _logger.LogDebug("Measurement {Index}: {Constellation} SV{SvId}, C/N0={Cno}, PR={PrMes:F3}m, Valid=PR:{PrValid}/CP:{CpValid}",
                i, gnssName, svId, cno, prMes, prValid, cpValid);

            satellites.Add(new
            {
                gnssId = gnssId,
                gnssName = gnssName,
                svId = svId,
                frequencyId = freqId,
                cno = cno,
                pseudorange = prMes,
                carrierPhase = cpMes,
                doppler = doMes,
                locktime = locktime,
                pseudorangeValid = prValid,
                carrierPhaseValid = cpValid,
                halfCycleValid = halfCyc,
                pseudorangeStdev = prStdev,
                carrierPhaseStdev = cpStdev,
                dopplerStdev = doStdev
            });
        }

        // Log constellation summary
        var constellationSummary = string.Join(", ",
            constellationCounts.Select(kv => $"{kv.Key}:{kv.Value}"));
        _logger.LogInformation("Parsed {TotalMeas} raw measurements: {ConstellationSummary}",
            satellites.Count, constellationSummary);

        var rawxData = new
        {
            rcvTow = rcvTow,
            week = week,
            leapSeconds = leapS,
            numMeasurements = numMeas,
            receiverStatus = recStat,
            satellites = satellites
        };

        try
        {
            await _hubContext.Clients.All.SendAsync("SatelliteUpdate", rawxData, stoppingToken);
            _logger.LogInformation("✅ Raw measurement data sent to frontend: {NumMeas} measurements", numMeas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send raw measurement data to frontend");
        }
    }

    private async Task ProcessMonVerMessage(byte[] data, CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("📋 Processing MON-VER message: {Length} bytes", data.Length);

            if (data.Length < 40)
            {
                _logger.LogWarning("MON-VER message too short: {Length} bytes", data.Length);
                return;
            }

            // Parse software version (first 30 bytes, null-terminated string)
            var swVersionBytes = data.Take(30).TakeWhile(b => b != 0).ToArray();
            var swVersion = global::System.Text.Encoding.ASCII.GetString(swVersionBytes);

            // Parse hardware version (next 10 bytes, null-terminated string)
            var hwVersionBytes = data.Skip(30).Take(10).TakeWhile(b => b != 0).ToArray();
            var hwVersion = global::System.Text.Encoding.ASCII.GetString(hwVersionBytes);

            _logger.LogInformation("🔍 ZED-X20P Software Version: {SwVersion}", swVersion);
            _logger.LogInformation("🔍 ZED-X20P Hardware Version: {HwVersion}", hwVersion);

            // Parse extensions (if any)
            if (data.Length > 40)
            {
                var remainingBytes = data.Skip(40).ToArray();
                var extensions = global::System.Text.Encoding.ASCII.GetString(remainingBytes.TakeWhile(b => b != 0).ToArray());
                if (!string.IsNullOrEmpty(extensions))
                {
                    _logger.LogInformation("🔍 ZED-X20P Extensions: {Extensions}", extensions);
                }
            }

            // Send version info to frontend
            var versionData = new
            {
                softwareVersion = swVersion,
                hardwareVersion = hwVersion,
                receiverType = "ZED-X20P"
            };

            await _hubContext.Clients.All.SendAsync("VersionUpdate", versionData, stoppingToken);
            _logger.LogInformation("✅ Version data sent to frontend");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MON-VER message");
        }
    }

    private static string GetGnssName(byte gnssId)
    {
        return gnssId switch
        {
            UbxConstants.GNSS_ID_GPS => "GPS",
            UbxConstants.GNSS_ID_SBAS => "SBAS",
            UbxConstants.GNSS_ID_GALILEO => "Galileo",
            UbxConstants.GNSS_ID_BEIDOU => "BeiDou",
            UbxConstants.GNSS_ID_IMES => "IMES",
            UbxConstants.GNSS_ID_QZSS => "QZSS",
            UbxConstants.GNSS_ID_GLONASS => "GLONASS",
            _ => $"Unknown({gnssId})"
        };
    }


    public override void Dispose()
    {
        _logger.LogInformation("GNSS Service disposing");
        base.Dispose();
    }
}