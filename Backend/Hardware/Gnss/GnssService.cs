using Backend.Hubs;
using Backend.Hardware.Bluetooth;
using Backend.Hardware.LoRa;
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
    private LoRaService? _loraService;
    private readonly GeoConfigurationManager _configurationManager;
    private readonly GnssFrameParser _frameParser;
    private SerialPort? _serialPort;
    private readonly List<byte> _dataBuffer = new();
    private readonly object _dataBufferLock = new();

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
    
    
    public GnssService(IHubContext<DataHub> hubContext, ILogger<GnssService> logger, GnssInitializer gnssInitializer, ILoggerFactory loggerFactory, IServiceProvider serviceProvider, GeoConfigurationManager configurationManager)
    {
        _hubContext = hubContext;
        _logger = logger;
        _gnssInitializer = gnssInitializer;
        _configurationManager = configurationManager;
        _dataFileWriter = new DataFileWriter("GNSS.raw", loggerFactory.CreateLogger<DataFileWriter>());
        _bluetoothService = new BluetoothStreamingService(loggerFactory.CreateLogger<BluetoothStreamingService>());
        _frameParser = new GnssFrameParser(loggerFactory.CreateLogger<GnssFrameParser>());

        // Get LoRa service from the service provider
        _loraService = serviceProvider.GetService<LoRaService>();

        // Subscribe to LoRa data events if in Receive mode
        if (_configurationManager.OperatingMode == OperatingMode.Receive && _loraService != null)
        {
            _loraService.DataReceived += OnLoRaDataReceived;
            _logger.LogInformation("📡 Subscribed to LoRa data events for RTCM forwarding to GNSS");
        }
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

        // Subscribe to serial port data received event
        _serialPort.DataReceived += OnSerialDataReceived;
        
        // Start background task for periodic rate updates
        _ = Task.Run(async () => await RateUpdateLoop(stoppingToken), stoppingToken);
        
        // Keep the service running until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
    }

    private async void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            await ReadAndProcessGnssDataAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in serial data received event handler");
        }
    }

    private async Task RateUpdateLoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateDataRatesAsync(stoppingToken);
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in rate update loop");
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

                lock (_dataBufferLock)
                {
                    for (int i = 0; i < bytesRead; i++)
                    {
                        _dataBuffer.Add(buffer[i]);
                    }
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
        lock (_dataBufferLock)
        {
            if (_dataBuffer.Count == 0)
            {
                _logger.LogDebug("📭 ProcessBufferedDataAsync: Buffer is empty");
                return;
            }
        }

        //_logger.LogInformation("🔄 ProcessBufferedDataAsync: Processing buffer with {Count} bytes", _dataBuffer.Count);

        // Trim runaway buffers (drop oldest)
        lock (_dataBufferLock)
        {
            if (_dataBuffer.Count > maxBufferBytes)
            {
                int toDrop = _dataBuffer.Count - maxBufferBytes;
                _logger.LogWarning("Buffer exceeded {Max} bytes; dropping {Drop} oldest bytes.", maxBufferBytes, toDrop);
                _dataBuffer.RemoveRange(0, toDrop);
            }
        }

        while (!stoppingToken.IsCancellationRequested && processed < maxMessagesPerLoop)
        {
            // Try to locate earliest valid frame among UBX / RTCM3 / NMEA
            bool frameFound;
            FrameKind kind;
            int start, totalLen, partialNeeded;
            
            lock (_dataBufferLock)
            {
                frameFound = _frameParser.TryFindNextFrame(_dataBuffer, out kind, out start, out totalLen, out partialNeeded);
                
                // Only log when no frames found and no partial frames (potential garbage)
                if (!frameFound && partialNeeded == 0)
                {
                    // Show first few bytes for debugging garbage data
                    var debugBytes = Math.Min(_dataBuffer.Count, 8);
                    var hexDump = string.Join(" ", _dataBuffer.Take(debugBytes).Select(b => $"{b:X2}"));
                    _logger.LogDebug("🔍 No frames found. Buffer: {HexDump} (total {Count} bytes)", hexDump, _dataBuffer.Count);
                }
            }
            
            if (!frameFound)
            { 
                // If we saw a plausible, but partial frame, wait for more data.
                if (partialNeeded > 0)
                {
                    _logger.LogDebug("Waiting for {Bytes} more bytes to complete a partial {Kind} frame.", partialNeeded, kind);
                    break;
                }

                // Otherwise drop a single garbage byte (don't clear entire buffer).
                lock (_dataBufferLock)
                {
                    if (_dataBuffer.Count > 0)
                    {
                        // Show more context data for analysis
                        var contextBytes = Math.Min(_dataBuffer.Count, 32);
                        var hexDump = string.Join(" ", _dataBuffer.Take(contextBytes).Select(b => $"{b:X2}"));
                        _logger.LogInformation("🗑️ No valid frames found, dropping 1 garbage byte (0x{Byte:X2}). Context (next {ContextCount} bytes): {HexDump}", 
                            _dataBuffer[0], contextBytes, hexDump);
                        _dataBuffer.RemoveAt(0);
                    }
                }
                break; // allow more data to arrive before spinning again
            }

            //_logger.LogInformation("✅ Found {Kind} frame at position {Start}, length {Length}", kind, start, totalLen);

            // Extract frame data within lock
            byte[] frame;
            lock (_dataBufferLock)
            {
                // Drop garbage before the frame
                if (start > 0)
                {
                    // Show hex dump of garbage data for analysis
                    var garbageBytes = _dataBuffer.Take(Math.Min(start, 32)).ToArray();
                    var hexDump = string.Join(" ", garbageBytes.Select(b => $"{b:X2}"));
                    _logger.LogInformation("🗑️ Dropping {Count} garbage bytes before {Kind} frame. Garbage data (first {ShowCount} bytes): {HexDump}", 
                        start, kind, garbageBytes.Length, hexDump);
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
                frame = _dataBuffer.GetRange(0, totalLen).ToArray();
                _dataBuffer.RemoveRange(0, totalLen);
            }

            try
            {
                switch (kind)
                {
                    case FrameKind.Ubx:
                        await ProcessUbx(frame, stoppingToken);
                        break;

                    case FrameKind.Rtcm3:
                        await ProcessRtcm3(frame, stoppingToken);
                        break;

                    case FrameKind.Nmea:
                        string nmea = System.Text.Encoding.ASCII.GetString(frame).TrimEnd('\r', '\n');
                        await ProcessNmea(nmea);
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


    private async Task ProcessRtcm3(byte[] completeMessage, CancellationToken stoppingToken)
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
            
            //_logger.LogInformation("📨 RTCM message received: Type={Type}, Length={Length}", messageType, completeMessage.Length);
            
            // Validate RTCM3 message type - standard types are typically 1000-1300 range
            var isValidRtcmType = (messageType >= 1000 && messageType <= 1300) || 
                                  (messageType >= 4000 && messageType <= 4100); // Some extended types
                                  
            if (!isValidRtcmType)
            {
                _logger.LogWarning("⚠️ Invalid RTCM3 message type {Type} - likely false positive", messageType);
                return;
            }
            
            var messageKey = $"RTCM3.{messageType}";

            // Parse RTCM 1005 for reference station position
            if (messageType == 1005)
            {
                //_logger.LogInformation("📡 RTCM 1005 detected! Parsing reference station position...");
                await Rtcm1005Parser.ProcessAsync(completeMessage, _hubContext, _logger, stoppingToken);
            }

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

            // Send RTCM3 message via LoRa
            if (_loraService != null)
            {
                try
                {
                    await _loraService.SendData(completeMessage);
                    _logger.LogDebug("📡 LoRa: Sent RTCM3 message type {Type} ({Length} bytes)", messageType, completeMessage.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "📡 LoRa: Failed to send RTCM3 message type {Type}", messageType);
                }
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

    private async Task ProcessUbx(byte[] completeMessage, CancellationToken stoppingToken)
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
            else if (messageClass == UbxConstants.CLASS_NAV && messageId == UbxConstants.NAV_DOP)
            {
                await DilutionOfPrecisionParser.ProcessAsync(data, _hubContext, _logger, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_NAV && messageId == UbxConstants.NAV_SVIN)
            {
                await SurveyInStatusParser.ProcessAsync(data, _hubContext, _logger, stoppingToken);
            }
            else if (messageClass == UbxConstants.CLASS_NAV && messageId == UbxConstants.NAV_SIG)
            {
                await NavigationSignalParser.ProcessAsync(data, _hubContext, _logger, stoppingToken);
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
            else if (messageClass == UbxConstants.CLASS_MON && messageId == UbxConstants.MON_COMMS)
            {
                await CommunicationStatusParser.ProcessAsync(data, _hubContext, _logger, stoppingToken);
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

            // Get LoRa rates if service is available
            double? loraInRate = _loraService?.CurrentReceiveRate;
            double? loraOutRate = _loraService?.CurrentSendRate;

            // Broadcast data rates
            await _hubContext.Clients.All.SendAsync("DataRatesUpdate", new DataRatesUpdate
            {
                KbpsGnssIn = _currentInRate,
                KbpsGnssOut = _currentOutRate,
                KbpsLoRaIn = loraInRate,
                KbpsLoRaOut = loraOutRate
            }, stoppingToken);

            _logger.LogDebug("Data rates updated - GNSS In: {InRate:F1} kbps, GNSS Out: {OutRate:F1} kbps, LoRa In: {LoRaIn:F1} kbps, LoRa Out: {LoRaOut:F1} kbps",
                _currentInRate, _currentOutRate, loraInRate ?? 0.0, loraOutRate ?? 0.0);
        }
    }

    // Method to track outgoing data (e.g., when sending RTCM corrections)
    public void TrackBytesSent(int bytesSent)
    {
        _bytesSent += bytesSent;
    }

    // Method to send RTCM data directly to GNSS port (for Receive mode)
    public async Task SendRtcmToGnss(byte[] rtcmData)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            _logger.LogWarning("Cannot send RTCM to GNSS - serial port not available");
            return;
        }

        try
        {
            _serialPort.Write(rtcmData, 0, rtcmData.Length);
            TrackBytesSent(rtcmData.Length);
            _logger.LogDebug("📤 Sent {Length} bytes of RTCM data to GNSS port", rtcmData.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send RTCM data to GNSS port");
        }
    }

    // Event handler for LoRa data received (forwards directly to GNSS)
    private async void OnLoRaDataReceived(object? sender, byte[] data)
    {
        try
        {
            _logger.LogDebug("📡 LoRa: Received {Length} bytes, forwarding directly to GNSS", data.Length);
            await SendRtcmToGnss(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding LoRa data to GNSS");
        }
    }

    private async Task ProcessNmea(string nmeaSentence)
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GNSS Service StopAsync starting");

        // Unsubscribe from serial port events
        if (_serialPort != null)
        {
            _serialPort.DataReceived -= OnSerialDataReceived;
            _logger.LogInformation("📡 Unsubscribed from serial port data events");
        }

        // Unsubscribe from LoRa events
        if (_loraService != null)
        {
            _loraService.DataReceived -= OnLoRaDataReceived;
            _logger.LogInformation("📡 Unsubscribed from LoRa data events");
        }

        _logger.LogInformation("GNSS Service stopping data file writer");
        await _dataFileWriter.StopAsync(cancellationToken);
        _logger.LogInformation("GNSS Service data file writer stopped");

        _logger.LogInformation("GNSS Service stopping bluetooth service");
        await _bluetoothService.StopAsync(cancellationToken);
        _logger.LogInformation("GNSS Service bluetooth service stopped");

        _logger.LogInformation("GNSS Service calling base.StopAsync");
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("GNSS Service StopAsync completed");
    }

    public override void Dispose()
    {
        _logger.LogInformation("GNSS Service disposing");
        _dataFileWriter.Dispose();
        _bluetoothService.Dispose();
        base.Dispose();
    }
}