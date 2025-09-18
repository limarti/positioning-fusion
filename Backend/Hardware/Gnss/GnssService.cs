using Backend.Hubs;
using Backend.Hardware.Bluetooth;
using Backend.Hardware.Gnss;
using Backend.Hardware.Gnss.Parsers;
using Backend.Storage;
using Backend.Configuration;
using Microsoft.AspNetCore.SignalR;
using System.IO.Ports;

namespace Backend.Hardware.Gnss;

public class GnssService : BackgroundService
{
    private readonly IHubContext<DataHub> _hubContext;
    private readonly ILogger<GnssService> _logger;
    private readonly GnssInitializer _gnssInitializer;
    private readonly DataFileWriter _dataFileWriter;
    private readonly BluetoothStreamingService _bluetoothService;
    private SerialPort? _serialPort;
    private readonly List<byte> _dataBuffer = new();

    // Data rate tracking
    private long _bytesReceived = 0;
    private long _bytesSent = 0;
    private DateTime _lastRateUpdate = DateTime.UtcNow;
    
    // UBX message frequency tracking with 5-second rolling window
    private readonly Dictionary<string, Queue<DateTime>> _messageTimestamps = new();
    private DateTime _lastMessageRateSend = DateTime.UtcNow;
    private const int RollingWindowSeconds = 5;
    private double _currentInRate = 0.0;
    private double _currentOutRate = 0.0;
    
    // Bluetooth streaming
    private DateTime _lastBluetoothSend = DateTime.UtcNow;
    
    // NAV-SVIN polling
    private DateTime _lastNavSvinPoll = DateTime.UtcNow;
    
    public GnssService(IHubContext<DataHub> hubContext, ILogger<GnssService> logger, GnssInitializer gnssInitializer, ILoggerFactory loggerFactory)
    {
        _hubContext = hubContext;
        _logger = logger;
        _gnssInitializer = gnssInitializer;
        _dataFileWriter = new DataFileWriter("GNSS.raw", loggerFactory.CreateLogger<DataFileWriter>());
        _bluetoothService = new BluetoothStreamingService(loggerFactory.CreateLogger<BluetoothStreamingService>());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GNSS Service started");

        // Start the data file writer
        _ = Task.Run(() => _dataFileWriter.StartAsync(stoppingToken), stoppingToken);

        // Start the Bluetooth streaming service
        _ = Task.Run(() => _bluetoothService.StartAsync(stoppingToken), stoppingToken);

        _serialPort = _gnssInitializer.GetSerialPort();
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            _logger.LogWarning("GNSS serial port not available - service will not collect data");

            // Send disconnected status to frontend
            await _hubContext.Clients.All.SendAsync("SatelliteUpdate", new SatelliteUpdate { Connected = false }, stoppingToken);

            // Send zero data rates since GNSS is disconnected
            await _hubContext.Clients.All.SendAsync("DataRatesUpdate", new DataRatesUpdate
            {
                KbpsGnssIn = 0.0,
                KbpsGnssOut = 0.0
            }, stoppingToken);

            return;
        }


        _logger.LogInformation("GNSS Service connected to {PortName} at {BaudRate} baud",
            _serialPort.PortName, _serialPort.BaudRate);

        // Time mode polling via CFG-VALGET not supported by this module
        // Status will be determined by presence of NAV-SVIN (active) or RTCM3 (completed) messages

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReadAndProcessGnssDataAsync(stoppingToken);
                await UpdateDataRatesAsync(stoppingToken);
                await PollNavSvinIfNeeded(stoppingToken);
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

                // Track bytes received for data rate calculation
                _bytesReceived += bytesRead;

                //_logger.LogInformation("📥 Read {BytesRead} bytes from GNSS, buffer now has {BufferSize} bytes", bytesRead, _dataBuffer.Count + bytesRead);

                for (int i = 0; i < bytesRead; i++)
                {
                    _dataBuffer.Add(buffer[i]);
                }

                await ProcessBufferedDataAsync(stoppingToken);
            }
            else
            {
                _logger.LogDebug("📭 No bytes available to read from GNSS");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading GNSS data");
        }
    }

    private async Task ProcessBufferedDataAsync(CancellationToken stoppingToken)
    {
        const int maxMessagesPerLoop = 50; // prevent infinite loops
        const int maxBufferBytes = 1 << 20; // 1 MiB cap (adjust if needed)
        int processed = 0;

        // Quick guard: nothing useful to do yet
        if (_dataBuffer.Count == 0)
        {
            _logger.LogDebug("📭 ProcessBufferedDataAsync: Buffer is empty");
            return;
        }

        //_logger.LogInformation("🔄 ProcessBufferedDataAsync: Processing buffer with {Count} bytes", _dataBuffer.Count);

        // Trim runaway buffers (drop oldest)
        if (_dataBuffer.Count > maxBufferBytes)
        {
            int toDrop = _dataBuffer.Count - maxBufferBytes;
            _logger.LogWarning("Buffer exceeded {Max} bytes; dropping {Drop} oldest bytes.", maxBufferBytes, toDrop);
            _dataBuffer.RemoveRange(0, toDrop);
        }

        while (!stoppingToken.IsCancellationRequested && processed < maxMessagesPerLoop)
        {
            // Try to locate earliest valid frame among UBX / RTCM3 / NMEA
            if (!TryFindNextFrame(out var kind, out int start, out int totalLen, out int partialNeeded))
            { 
                // If we saw a plausible, but partial frame, wait for more data.
                if (partialNeeded > 0)
                {
                    _logger.LogDebug("Waiting for {Bytes} more bytes to complete a partial {Kind} frame.", partialNeeded, kind);
                    break;
                }

                // Otherwise drop a single garbage byte (don't clear entire buffer).
                if (_dataBuffer.Count > 0)
                {
                    _logger.LogDebug("🗑️ No valid frames found, dropping 1 garbage byte (0x{Byte:X2})", _dataBuffer[0]);
                    _dataBuffer.RemoveAt(0);
                }
                break; // allow more data to arrive before spinning again
            }

            //_logger.LogInformation("✅ Found {Kind} frame at position {Start}, length {Length}", kind, start, totalLen);

            // Drop garbage before the frame
            if (start > 0)
            {
                _logger.LogDebug("🗑️ Dropping {Count} garbage bytes before {Kind} frame", start, kind);
                _dataBuffer.RemoveRange(0, start);
            }

            // If the frame is partial, wait
            if (_dataBuffer.Count < totalLen)
            {
                int need = totalLen - _dataBuffer.Count;
                _logger.LogDebug("Partial {Kind} frame detected. Need {Need} more bytes.", kind, need);
                break;
            }

            // Extract and remove frame
            var frame = _dataBuffer.GetRange(0, totalLen).ToArray();
            _dataBuffer.RemoveRange(0, totalLen);

            try
            {
                switch (kind)
                {
                    case FrameKind.Ubx:
                        //_logger.LogInformation("📦 Processing UBX frame: Length={Len} bytes", frame.Length);
                        await ProcessUbxMessage(frame, stoppingToken);
                        break;

                    case FrameKind.Rtcm3:
                        // Decode message type (first 12 bits of payload)
                        if (frame.Length >= 5)
                        {
                            ushort msgType = (ushort)((frame[3] << 4) | (frame[4] >> 4));
                            
                            // Validate RTCM3 message type - standard types are typically 1000-1300 range
                            var isValidRtcmType = (msgType >= 1000 && msgType <= 1300) || 
                                                  (msgType >= 4000 && msgType <= 4100); // Some extended types
                            
                            if (isValidRtcmType)
                            {
                                await ProcessRtcm3Message(frame, stoppingToken);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ RTCM3 frame with invalid message type {Type} (Length={Len}) - skipping", msgType, frame.Length);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ RTCM3 frame too short to extract message type (Length={Len})", frame.Length);
                        }
                        break;

                    case FrameKind.Nmea:
                        // frame is ASCII including trailing \r\n; validate already done.
                        string nmea = System.Text.Encoding.ASCII.GetString(frame).TrimEnd('\r', '\n');
                        //_logger.LogInformation("🛰️ Processing NMEA frame: {Sentence}", nmea.Length > 20 ? nmea.Substring(0, 20) + "..." : nmea);
                        TrackNmeaMessage(nmea);
                        await SendNmeaViaBluetooth(nmea);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing {Kind} frame.", kind);
            }

            processed++;
        }
    }

    // ===== local helpers =====

    enum FrameKind { Ubx, Rtcm3, Nmea }

    bool TryFindNextFrame(out FrameKind kind, out int start, out int totalLen, out int partialNeeded)
    {
        kind = default;
        start = -1;
        totalLen = -1;
        partialNeeded = 0;

        var ubx = FindUbxCandidate();
        var rtcm = FindRtcm3Candidate();
        var nmea = FindNmeaCandidate();

        (FrameKind k, int s, int t, int partial)? winner = null;

        void Consider((FrameKind k, int s, int t, int partial)? c)
        {
            if (c is { } x && x.s >= 0)
                winner = winner is null || x.s < winner.Value.s ? x : winner;
        }

        Consider(ubx);
        Consider(rtcm);
        Consider(nmea);

        if (winner is null)
            return false;

        (kind, start, totalLen, partialNeeded) = winner.Value;
        return true;
    }

    (FrameKind k, int s, int t, int partial)? FindUbxCandidate()
    {
        // UBX: 0xB5 0x62 [cls][id][lenL][lenH] payload ... [CK_A][CK_B]
        for (int i = 0; i + 5 < _dataBuffer.Count; i++)
        {
            if (_dataBuffer[i] != 0xB5 || _dataBuffer[i + 1] != 0x62) continue;

            // need at least header to read length
            if (i + 6 > _dataBuffer.Count) 
            {
                _logger.LogDebug("⏳ UBX partial header at {Pos}: need {Need} more bytes", i, 6 - (i + 6 - _dataBuffer.Count));
                return (FrameKind.Ubx, i, 6, 6 - _dataBuffer.Count + i);
            }

            int len = _dataBuffer[i + 4] | (_dataBuffer[i + 5] << 8);
            if (len < 0 || len > 1024) 
            {
                _logger.LogDebug("🚫 UBX candidate at {Pos}: invalid length {Len}", i, len);
                continue;
            }

            int total = 6 + len + 2;
            if (i + total > _dataBuffer.Count)
            {
                _logger.LogDebug("⏳ UBX partial frame at {Pos}: need {Need} more bytes (payload={PayloadLen}, total={Total})", 
                    i, (i + total) - _dataBuffer.Count, len, total);
                return (FrameKind.Ubx, i, total, (i + total) - _dataBuffer.Count);
            }

            if (ValidateUbxChecksum(i, total))
            {
                _logger.LogDebug("✅ Valid UBX frame found at {Pos}: payload={PayloadLen}, total={Total}", i, len, total);
                return (FrameKind.Ubx, i, total, 0);
            }
            else
            {
                _logger.LogDebug("❌ UBX candidate at {Pos}: checksum validation failed (payload={PayloadLen})", i, len);
            }

            // bad checksum — skip this sync and keep scanning
        }
        return null;
    }

    (FrameKind k, int s, int t, int partial)? FindRtcm3Candidate()
    {
        // RTCM3: 0xD3 [Rsv(6b)|Len(10b)] payload CRC24Q (3 bytes)
        for (int i = 0; i + 2 < _dataBuffer.Count; i++)
        {
            if (_dataBuffer[i] != 0xD3) continue;

            byte b1 = _dataBuffer[i + 1];
            byte b2 = _dataBuffer[i + 2];

            // Upper 6 bits of b1 must be 0
            if ((b1 & 0xFC) != 0)
            {
                _logger.LogDebug("🚫 RTCM3 candidate at {Pos}: invalid reserved bits in b1=0x{B1:X2}", i, b1);
                continue;
            }

            int payloadLen = ((b1 & 0x03) << 8) | b2;
            if (payloadLen <= 0 || payloadLen > 1024)
            {
                _logger.LogDebug("🚫 RTCM3 candidate at {Pos}: invalid payload length {Len}", i, payloadLen);
                continue;
            }

            int total = 3 + payloadLen + 3;
            if (i + total > _dataBuffer.Count)
            {
                _logger.LogDebug("⏳ RTCM3 partial frame at {Pos}: need {Need} more bytes (payload={PayloadLen}, total={Total})", 
                    i, (i + total) - _dataBuffer.Count, payloadLen, total);
                return (FrameKind.Rtcm3, i, total, (i + total) - _dataBuffer.Count);
            }

            if (ValidateRtcmCrc24Q(i, total))
            {
                return (FrameKind.Rtcm3, i, total, 0);
            }
            else
            {
                _logger.LogDebug("❌ RTCM3 candidate at {Pos}: CRC validation failed (payload={PayloadLen})", i, payloadLen);
            }
            // bad CRC — keep scanning
        }
        return null;
    }

    (FrameKind k, int s, int t, int partial)? FindNmeaCandidate()
    {
        // NMEA: '$' ... *hh \r\n  — ASCII only, checksum validated.
        // We only try this if there is clearly a '$' in view.
        int dollar = _dataBuffer.IndexOf((byte)'$');
        if (dollar < 0) return null;

        // Must end with CRLF to be complete
        int cr = _dataBuffer.IndexOf((byte)'\r', dollar + 1);
        if (cr < 0 || cr + 1 >= _dataBuffer.Count) return (FrameKind.Nmea, dollar, 0, 1); // partial; need at least CRLF

        if (_dataBuffer[cr + 1] != (byte)'\n') return null;

        int end = cr + 2;
        int len = end - dollar;
        if (len < 9) return null; // too short to be valid

        // Validate ASCII and checksum
        var span = _dataBuffer.GetRange(dollar, len).ToArray();
        if (!IsAscii(span)) return null;
        if (!ValidateNmeaChecksum(span)) return null;

        return (FrameKind.Nmea, dollar, len, 0);
    }

    bool ValidateUbxChecksum(int start, int total)
    {
        byte ckA = 0, ckB = 0;
        int payloadLen = _dataBuffer[start + 4] | (_dataBuffer[start + 5] << 8);
        int end = start + 6 + payloadLen;
        for (int j = start + 2; j < end; j++)
        {
            ckA = (byte)(ckA + _dataBuffer[j]);
            ckB = (byte)(ckB + ckA);
        }
        return ckA == _dataBuffer[end] && ckB == _dataBuffer[end + 1];
    }

    bool ValidateRtcmCrc24Q(int start, int total)
    {
        // CRC over header(3) + payload, excluding the 3 CRC bytes at the end
        int crcLen = total - 3;
        uint calc = Crc24Q(_dataBuffer, start, crcLen);
        uint got = (uint)(_dataBuffer[start + total - 3] << 16 | _dataBuffer[start + total - 2] << 8 | _dataBuffer[start + total - 1]);
        return calc == got;
    }

    static bool IsAscii(byte[] bytes)
    {
        foreach (var b in bytes)
        {
            if (b is < 0x09 or > 0x7E && b != 0x0D && b != 0x0A) return false;
        }
        return true;
    }

    static bool ValidateNmeaChecksum(byte[] asciiWithCrlf)
    {
        // Expect: $XXXX*HH\r\n
        // Find '*'
        int star = Array.LastIndexOf(asciiWithCrlf, (byte)'*');
        if (star <= 0) return false;
        if (star + 2 >= asciiWithCrlf.Length) return false; // need two hex chars + CRLF

        byte cs = 0;
        for (int i = 1; i < star; i++)
            cs ^= asciiWithCrlf[i];

        bool hexOk = TryHexByte(asciiWithCrlf[star + 1], asciiWithCrlf[star + 2], out byte got);
        if (!hexOk) return false;

        return cs == got;
    }

    static bool TryHexByte(byte hi, byte lo, out byte val)
    {
        val = 0;
        int h = FromHex(hi);
        int l = FromHex(lo);
        if (h < 0 || l < 0) return false;
        val = (byte)((h << 4) | l);
        return true;

        static int FromHex(byte c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }
    }

    static uint Crc24Q(List<byte> buf, int start, int length)
    {
        // Polynomial 0x1864CFB, init 0x000000 (per RTCM standard)
        const uint poly = 0x1864CFB;
        uint crc = 0;
        for (int i = 0; i < length; i++)
        {
            crc ^= (uint)buf[start + i] << 16;
            for (int b = 0; b < 8; b++)
            {
                crc <<= 1;
                if ((crc & 0x1000000) != 0)
                    crc ^= poly;
            }
            crc &= 0xFFFFFF;
        }
        return crc & 0xFFFFFF;
    }

    private async Task ProcessRtcm3Message(byte[] completeMessage, CancellationToken stoppingToken)
    {
        try
        {
            if (completeMessage.Length < UbxConstants.RTCM3_MIN_LENGTH)
                return;

            // Extract RTCM3 message type (first 12 bits of payload)
            if (completeMessage.Length < 5)
                return;

            // RTCM3 message type: first 12 bits of payload (after 3-byte header)
            // Payload starts at byte 3, message type is the first 12 bits of payload
            var messageType = (ushort)((completeMessage[3] << 4) | (completeMessage[4] >> 4));
            
            // Validate RTCM3 message type - standard types are typically 1000-1300 range
            var isValidRtcmType = (messageType >= 1000 && messageType <= 1300) || 
                                  (messageType >= 4000 && messageType <= 4100); // Some extended types
                                  
            if (!isValidRtcmType)
            {
                _logger.LogWarning("⚠️ Invalid RTCM3 message type {Type} - likely false positive", messageType);
                return;
            }
            
            var messageKey = $"RTCM3.{messageType}";

            // Track message frequency with timestamps
            var now = DateTime.UtcNow;
            lock (_messageTimestamps)
            {
                if (!_messageTimestamps.ContainsKey(messageKey))
                {
                    _messageTimestamps[messageKey] = new Queue<DateTime>();
                }
                _messageTimestamps[messageKey].Enqueue(now);
            }

            // Send message rates to frontend every second
            if ((now - _lastMessageRateSend).TotalMilliseconds >= 1000)
            {
                await SendMessageRatesToFrontend();
                _lastMessageRateSend = now;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RTCM3 message");
        }
    }

    // NMEA fallback removed - UBX binary messages only

    private async Task ProcessUbxMessage(byte[] completeMessage, CancellationToken stoppingToken)
    {
        try
        {
            var messageClass = completeMessage[2];
            var messageId = completeMessage[3];
            var length = (ushort)(completeMessage[4] | (completeMessage[5] << 8));
            var data = completeMessage.AsSpan(6, length).ToArray();

            _logger.LogDebug("Processing UBX message: Class=0x{Class:X2}, ID=0x{Id:X2}, Length={Length}",
                messageClass, messageId, data.Length);

            // Log raw GNSS UBX message data to file (complete UBX frame)
            _dataFileWriter.WriteData(completeMessage);

            if (messageClass == UbxConstants.CLASS_NAV && messageId == UbxConstants.NAV_SAT)
            {
                await NavigationSatelliteParser.ProcessAsync(data, _hubContext, _logger, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_NAV && messageId == UbxConstants.NAV_PVT)
            {
                await PositionVelocityTimeParser.ProcessAsync(data, _hubContext, _logger, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_NAV && messageId == UbxConstants.NAV_SVIN)
            {
                await SurveyInStatusParser.ProcessAsync(data, _hubContext, _logger, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_CFG)
            {
                _logger.LogInformation("📋 CFG message received: ID=0x{Id:X2}, Length={Length}", messageId, data.Length);
                if (messageId == UbxConstants.CFG_VALGET)
                {
                    _logger.LogInformation("Processing CFG-VALGET response...");
                    ProcessTimeModeResponse(data);
                }
                // Log configuration responses for debugging
            }
            else if (messageClass == UbxConstants.CLASS_MON && messageId == UbxConstants.MON_VER)
            {
                await ReceiverVersionParser.ProcessAsync(data, _hubContext, _logger, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_RXM && messageId == UbxConstants.RXM_SFRBX)
            {
                await BroadcastDataParser.ProcessAsync(data, _hubContext, _logger, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_RXM && messageId == UbxConstants.RXM_RAWX)
            {
                // RXM-RAWX: Raw measurement data - received but not parsed
            }
            else if (messageClass == UbxConstants.CLASS_ACK)
            {
                if (messageId == UbxConstants.ACK_ACK)
                {
                    _logger.LogInformation("✅ UBX command acknowledged");
                }
                else if (messageId == UbxConstants.ACK_NAK)
                {
                    if (data.Length >= 2)
                    {
                        var nakClass = data[0];
                        var nakId = data[1];
                        _logger.LogWarning("❌ UBX command rejected (NAK received) for Class=0x{Class:X2}, ID=0x{Id:X2}", nakClass, nakId);
                    }
                    else
                    {
                        _logger.LogWarning("❌ UBX command rejected (NAK received)");
                    }
                }
            }
            else
            {
                // Log unknown/unparsed UBX messages
                _logger.LogInformation("❓ Unknown UBX message: Class=0x{Class:X2}, ID=0x{Id:X2}, Length={Length} bytes", 
                    messageClass, messageId, data.Length);

                // Log first few bytes for debugging
                if (data.Length > 0)
                {
                    var sampleData = string.Join(" ", data.Take(Math.Min(data.Length, 16)).Select(b => $"{b:X2}"));
                    _logger.LogDebug("UBX message data (first 16 bytes): {SampleData}", sampleData);
                }
            }

            var className = Parsers.GnssParserUtils.GetConstantName(messageClass);
            var messageName = Parsers.GnssParserUtils.GetConstantName(messageId);
            var messageKey = $"{className}.{messageName}";
            
            // Track message frequency with timestamps
            var now = DateTime.UtcNow;
            lock (_messageTimestamps)
            {
                if (!_messageTimestamps.ContainsKey(messageKey))
                {
                    _messageTimestamps[messageKey] = new Queue<DateTime>();
                }
                _messageTimestamps[messageKey].Enqueue(now);
            }
            
            // Send message rates to frontend every second
            if ((now - _lastMessageRateSend).TotalMilliseconds >= 1000)
            {
                await SendMessageRatesToFrontend();
                _lastMessageRateSend = now;
            }
        }
        catch (Exception ex)
        {
            var messageClass = completeMessage.Length > 2 ? completeMessage[2] : (byte)0;
            var messageId = completeMessage.Length > 3 ? completeMessage[3] : (byte)0;
            _logger.LogError(ex, "Error processing UBX message Class=0x{Class:X2}, ID=0x{Id:X2}", messageClass, messageId);
        }
    }

    private void TrackNmeaMessage(string nmeaSentence)
    {
        try
        {
            // Extract message type from NMEA sentence (e.g., "$GPGGA" -> "GPGGA")
            if (nmeaSentence.Length >= 6)
            {
                var messageType = nmeaSentence.Substring(1, 5); // Skip $ and take next 5 characters
                var messageKey = $"NMEA.{messageType}";
                
                // Track message frequency with timestamps
                var now = DateTime.UtcNow;
                lock (_messageTimestamps)
                {
                    if (!_messageTimestamps.ContainsKey(messageKey))
                    {
                        _messageTimestamps[messageKey] = new Queue<DateTime>();
                    }
                    _messageTimestamps[messageKey].Enqueue(now);
                }
               
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking NMEA message: {Sentence}", nmeaSentence);
        }
    }

    private async Task SendMessageRatesToFrontend()
    {
        var now = DateTime.UtcNow;
        var cutoffTime = now.AddSeconds(-RollingWindowSeconds);
        var messageRates = new Dictionary<string, double>();

        lock (_messageTimestamps)
        {
            foreach (var (messageType, timestamps) in _messageTimestamps)
            {
                // Remove timestamps older than 5 seconds
                while (timestamps.Count > 0 && timestamps.Peek() < cutoffTime)
                {
                    timestamps.Dequeue();
                }

                // Calculate rate: messages in last 5 seconds / 5 seconds
                var rate = timestamps.Count / (double)RollingWindowSeconds;
                messageRates[messageType] = rate;
            }
        }

        // Send to frontend via SignalR
        await _hubContext.Clients.All.SendAsync("MessageRatesUpdate", new MessageRatesUpdate
        {
            MessageRates = messageRates,
            Timestamp = now
        });
    }

    private async Task UpdateDataRatesAsync(CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;
        var timeDelta = (now - _lastRateUpdate).TotalSeconds;

        // Update rates every second
        if (timeDelta >= 1.0)
        {
            // Calculate rates in kbps (kilobits per second)
            _currentInRate = (_bytesReceived * 8.0) / (timeDelta * 1000.0);
            _currentOutRate = (_bytesSent * 8.0) / (timeDelta * 1000.0);

            // Reset counters
            _bytesReceived = 0;
            _bytesSent = 0;
            _lastRateUpdate = now;

            // Broadcast data rates
            await _hubContext.Clients.All.SendAsync("DataRatesUpdate", new DataRatesUpdate
            {
                KbpsGnssIn = _currentInRate,
                KbpsGnssOut = _currentOutRate
            }, stoppingToken);

            _logger.LogDebug("Data rates updated - In: {InRate:F1} kbps, Out: {OutRate:F1} kbps",
                _currentInRate, _currentOutRate);
        }
    }

    // Method to track outgoing data (e.g., when sending RTCM corrections)
    public void TrackBytesSent(int bytesSent)
    {
        _bytesSent += bytesSent;
    }

    private async Task SendNmeaViaBluetooth(string nmeaSentence)
    {
        try
        {
            // Convert NMEA sentence to bytes with proper line endings
            var nmeaBytes = global::System.Text.Encoding.ASCII.GetBytes(nmeaSentence + "\r\n");
            
            // Log hex dump for debugging
            var hexDump = string.Join(" ", nmeaBytes.Select(b => $"{b:X2}"));
            
            // Log attempt to send
            //_logger.LogInformation("📤 Attempting to send NMEA via Bluetooth ({Bytes} bytes): {Sentence}", nmeaBytes.Length, nmeaSentence);
            
            // Send via Bluetooth service
            await _bluetoothService.SendData(nmeaBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send NMEA sentence via Bluetooth: {Sentence}", nmeaSentence);
        }
    }

    private void ProcessTimeModeResponse(byte[] data)
    {
        try
        {
            if (data.Length < 8)
            {
                _logger.LogWarning("CFG-VALGET response too short: {Length} bytes", data.Length);
                return;
            }

            // Skip version, layer, position (first 4 bytes)
            // Key ID is next 4 bytes (should be TMODE_MODE)
            var keyId = BitConverter.ToUInt32(data, 4);
            
            if (keyId == UbxConstants.TMODE_MODE && data.Length >= 9)
            {
                var timeModeValue = data[8];
                var timeModeName = timeModeValue switch
                {
                    UbxConstants.TMODE_DISABLED => "Disabled",
                    UbxConstants.TMODE_SURVEY_IN => "Survey-In", 
                    UbxConstants.TMODE_FIXED => "Fixed",
                    _ => $"Unknown ({timeModeValue})"
                };

                _logger.LogInformation("⏱️ Current Time Mode: {TimeMode} (value={Value})", timeModeName, timeModeValue);
                
                if (timeModeValue == UbxConstants.TMODE_SURVEY_IN)
                {
                    _logger.LogInformation("✅ Module is in Survey-In mode - should be sending NAV-SVIN messages");
                }
                else if (timeModeValue == UbxConstants.TMODE_DISABLED)
                {
                    _logger.LogWarning("❌ Module time mode is DISABLED - Survey-In not running!");
                }
                else if (timeModeValue == UbxConstants.TMODE_FIXED)
                {
                    _logger.LogInformation("🎯 Module is in FIXED mode - Survey-In completed, should send RTCM3");
                }
            }
            else
            {
                _logger.LogDebug("CFG-VALGET response for different key: 0x{KeyId:X8}", keyId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing time mode response");
        }
    }

    private async Task PollNavSvinIfNeeded(CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastNavSvinPoll).TotalSeconds >= 5.0)
        {
            PollNavSvin();
            _lastNavSvinPoll = now;
        }
    }

    // Send a UBX-NAV-SVIN poll (no payload)
    private void PollNavSvin()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return;

        var frame = CreateUbxMessage(UbxConstants.CLASS_NAV, UbxConstants.NAV_SVIN, Array.Empty<byte>());
        _serialPort.Write(frame, 0, frame.Length);
        _logger.LogDebug("🔍 Polled UBX-NAV-SVIN");
    }

    private byte[] CreateUbxMessage(byte messageClass, byte messageId, byte[] payload)
    {
        var message = new List<byte>();

        // Sync chars
        message.Add(UbxConstants.SYNC_CHAR_1);
        message.Add(UbxConstants.SYNC_CHAR_2);

        // Message class and ID
        message.Add(messageClass);
        message.Add(messageId);

        // Payload length (little-endian)
        var length = (ushort)payload.Length;
        message.Add((byte)(length & 0xFF));
        message.Add((byte)(length >> 8));

        // Payload
        message.AddRange(payload);

        // Calculate checksum (Fletcher-8 algorithm)
        byte ck_a = 0, ck_b = 0;
        for (int i = 2; i < message.Count; i++)
        {
            ck_a += message[i];
            ck_b += ck_a;
        }
        message.Add(ck_a);
        message.Add(ck_b);

        return message.ToArray();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping GNSS Service");
        await _dataFileWriter.StopAsync(cancellationToken);
        await _bluetoothService.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _logger.LogInformation("GNSS Service disposing");
        _dataFileWriter.Dispose();
        _bluetoothService.Dispose();
        base.Dispose();
    }
}