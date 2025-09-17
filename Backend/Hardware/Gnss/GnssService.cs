using Backend.Hubs;
using Backend.Hardware.Gnss;
using Backend.Hardware.Gnss.Parsers;
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

    // Data rate tracking
    private long _bytesReceived = 0;
    private long _bytesSent = 0;
    private DateTime _lastRateUpdate = DateTime.UtcNow;
    private double _currentInRate = 0.0;
    private double _currentOutRate = 0.0;

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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReadAndProcessGnssDataAsync(stoppingToken);
                await UpdateDataRatesAsync(stoppingToken);
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
                await NavigationSatelliteParser.ProcessAsync(data, _hubContext, _logger, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_NAV && messageId == UbxConstants.NAV_PVT)
            {
                await PositionVelocityTimeParser.ProcessAsync(data, _hubContext, _logger, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_CFG)
            {
                //_logger.LogInformation("📋 Received CFG message: ID=0x{Id:X2}, Length={Length}", messageId, data.Length);
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
                //_logger.LogInformation("🔍 Received UBX message Class=0x{Class:X2}, ID=0x{Id:X2}, Length={Length} bytes", messageClass, messageId, data.Length);

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

    public override void Dispose()
    {
        _logger.LogInformation("GNSS Service disposing");
        base.Dispose();
    }
}