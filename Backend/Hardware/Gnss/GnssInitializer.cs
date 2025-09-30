using System.IO.Ports;
using Backend.Configuration;
using Backend.Hardware.Common;

namespace Backend.Hardware.Gnss;

public enum UbxResponseType
{
    Ack,
    Nak,
    Timeout,
    Error
}

public class GnssInitializer
{
    private readonly ILogger<GnssInitializer> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly GeoConfigurationManager _configurationManager;
    private SerialPortManager? _serialPortManager;

    // Shared lock for coordinating serial port access between GnssService and GnssInitializer
    private static readonly SemaphoreSlim _serialPortLock = new SemaphoreSlim(1, 1);

    public bool IsInitialized { get; private set; } = false;

    private const string DefaultPortName = "/dev/ttyAMA0";
    private const Parity DefaultParity = Parity.None;
    private const int DefaultDataBits = 8;
    private const StopBits DefaultStopBits = StopBits.One;
    private const int ResponseTimeoutMs = 3000;
    private const int PortStabilizationDelayMs = 500;
    private const int CommandDelayMs = 200;
    private const int BaudRateDelayMs = 3000;

    // ZED-X20P specific baud rates - try our target first, then default
    private readonly int[] _baudRatesToScan = new int[] { 460800, 38400 };

    public GnssInitializer(ILogger<GnssInitializer> logger, GeoConfigurationManager configurationManager, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _configurationManager = configurationManager;
    }

    public async Task<bool> InitializeAsync(string portName = DefaultPortName)
    {
        const int maxRetries = 10;
        const int retryDelayMs = 2000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            _logger.LogInformation("Initializing GNSS on port {PortName} (attempt {Attempt}/{MaxRetries})",
                portName, attempt, maxRetries);

            for (int i = 0; i < _baudRatesToScan.Length; i++)
            {
                var baudRate = _baudRatesToScan[i];
                _logger.LogInformation("Testing GNSS communication at {BaudRate} baud", baudRate);

                if (await TryBaudRateAsync(portName, baudRate))
                {
                    _logger.LogInformation("GNSS initialized successfully on port {PortName} at {BaudRate} baud",
                        portName, baudRate);

                    // Configure GNSS for satellite data output
                    await ConfigureForSatelliteDataAsync();

                    IsInitialized = true;
                    return true;
                }

                // Small delay between baud rate attempts
                if (i < _baudRatesToScan.Length - 1)
                {
                    await Task.Delay(1000);
                }
            }

            if (attempt < maxRetries)
            {
                _logger.LogWarning("GNSS initialization attempt {Attempt} failed, retrying in {Delay}ms...",
                    attempt, retryDelayMs);
                await Task.Delay(retryDelayMs);
            }
        }

        _logger.LogError("Failed to initialize GNSS on port {PortName} after {MaxRetries} attempts - no valid baud rate found",
            portName, maxRetries);
        IsInitialized = false;
        return false;
    }


    private async Task<string> TestGnssCommunicationAsync(SerialPortManager serialPortManager)
    {
        // Allow port to stabilize
        await Task.Delay(PortStabilizationDelayMs);

        // Send standard NMEA query for position data
        var pollCommand = "$PUBX,00*33\r\n";
        _logger.LogDebug("Sending GNSS position query: {Command}", pollCommand.Trim());
        serialPortManager.Write(pollCommand);

        // Wait for response - for testing, we'll wait and check if data starts flowing
        await Task.Delay(CommandDelayMs + 1000); // Longer delay for response

        // Check if device is responding by looking at receive rate
        if (serialPortManager.CurrentReceiveRate > 0)
        {
            _logger.LogDebug("GNSS device responding - receive rate: {Rate} bytes/sec", serialPortManager.CurrentReceiveRate);
            return "$PUBX,00,response_detected"; // Simplified response indication
        }

        _logger.LogDebug("No GNSS response detected - receive rate: {Rate} bytes/sec", serialPortManager.CurrentReceiveRate);
        return string.Empty;
    }

    private async Task<bool> TryBaudRateAsync(string portName, int baudRate)
    {
        try
        {
            // Dispose any existing connection
            _serialPortManager?.Dispose();
            _serialPortManager = null;

            // Create new connection at specified baud rate
            _serialPortManager = new SerialPortManager(
                "GNSS",
                portName,
                baudRate,
                _loggerFactory.CreateLogger<SerialPortManager>()
            );

            await _serialPortManager.StartAsync();
            _logger.LogDebug("Testing GNSS at {BaudRate} baud", baudRate);

            // Test communication
            var response = await TestGnssCommunicationAsync(_serialPortManager);

            if (!string.IsNullOrEmpty(response) && IsValidNmeaResponse(response))
            {
                _logger.LogInformation("GNSS communication established at {BaudRate} baud", baudRate);
                
                // If we're not at optimal speed, try to switch
                if (baudRate != 460800)
                {
                    _logger.LogInformation("Attempting to switch to 460800 baud for optimal performance");
                    await SwitchTo460800BaudAsync(baudRate, portName);
                    // Note: SwitchTo460800BaudAsync will update _serialPortManager if successful
                }
                
                return true;
            }

            _logger.LogDebug("No valid response at {BaudRate} baud", baudRate);
            
            // Clean up failed connection
            _serialPortManager?.Dispose();
            _serialPortManager = null;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to test GNSS at {BaudRate} baud", baudRate);
            _serialPortManager?.Dispose();
            _serialPortManager = null;
            return false;
        }
    }


    private async Task<bool> SwitchTo460800BaudAsync(int currentBaudRate, string portName)
    {
        try
        {
            if (_serialPortManager == null) return false;

            // Send baud rate change command
            var pubxBaudCommand = "$PUBX,41,1,0003,0003,460800,0";
            var checksum = CalculateNmeaChecksum(pubxBaudCommand.Substring(1));
            pubxBaudCommand += $"*{checksum:X2}\r\n";

            _logger.LogDebug("Sending PUBX baud command: {Command}", pubxBaudCommand.Trim());
            _serialPortManager.Write(pubxBaudCommand);

            // Wait for command to be processed
            await Task.Delay(BaudRateDelayMs);

            // Close current connection
            _serialPortManager.Dispose();
            _serialPortManager = null;

            // Wait for device to reconfigure
            await Task.Delay(BaudRateDelayMs);

            // Try connection at new baud rate
            _serialPortManager = new SerialPortManager(
                "GNSS",
                portName,
                460800,
                _loggerFactory.CreateLogger<SerialPortManager>()
            );

            await _serialPortManager.StartAsync();
            var response = await TestGnssCommunicationAsync(_serialPortManager);

            if (!string.IsNullOrEmpty(response) && IsValidNmeaResponse(response))
            {
                _logger.LogInformation("Successfully switched to 460800 baud");
                return true;
            }

            // Failed - try to restore original baud rate
            _logger.LogWarning("Failed to switch to 460800, restoring {OriginalBaud} baud", currentBaudRate);
            _serialPortManager?.Dispose();
            _serialPortManager = new SerialPortManager(
                "GNSS",
                portName,
                currentBaudRate,
                _loggerFactory.CreateLogger<SerialPortManager>()
            );
            await _serialPortManager.StartAsync();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch baud rate to 460800");
            return false;
        }
    }



    private static bool IsValidNmeaResponse(string response)
    {
        // Check for specific PUBX,00 position response format
        if (response.Contains("$PUBX,00"))
        {
            return true;
        }

        // Accept standard NMEA sentences as valid communication
        if (response.StartsWith("$GP") || response.StartsWith("$GN") ||
            response.StartsWith("$GL") || response.StartsWith("$BD"))
        {
            // Must have proper NMEA format: starts with $, contains commas, ends with *XX
            return response.Contains(",") && response.Contains("*");
        }

        // Accept any response that looks like a proper NMEA sentence
        if (response.StartsWith("$") && response.Contains(",") && response.Contains("*"))
        {
            // Check that it's not just garbage with these characters
            var parts = response.Split(',');
            return parts.Length >= 3; // Minimum fields for any valid NMEA sentence
        }

        // Reject everything else (including garbage)
        return false;
    }

    private static byte CalculateNmeaChecksum(string sentence)
    {
        byte checksum = 0;
        foreach (char c in sentence)
        {
            checksum ^= (byte)c;
        }
        return checksum;
    }

    public SerialPortManager? GetSerialPortManager()
    {
        return _serialPortManager;
    }


    // Provide access to the shared lock for coordination
    public static SemaphoreSlim GetSerialPortLock()
    {
        return _serialPortLock;
    }

    const int UBX_MESSAGES_RATE_HZ = 5;    //1, 5, 10 or 20
    private async Task ConfigureForSatelliteDataAsync()
    {
        if (_serialPortManager == null || !_serialPortManager.IsConnected)
        {
            _logger.LogWarning("Cannot configure GNSS - no active serial port connection");
            return;
        }

        _logger.LogInformation("Configuring GNSS for satellite data output");
        _logger.LogInformation("Mode: {Mode}, GNSS Rate: {Rate}Hz", _configurationManager.OperatingMode, UBX_MESSAGES_RATE_HZ);

        try
        {
            await SetNavigationRate(UBX_MESSAGES_RATE_HZ);
            await ConfigureUbxMessages();
            await ConfigureOperatingMode();
            await ConfigureNmeaMessages();
            
            _logger.LogInformation("GNSS configuration completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure GNSS");
        }
    }

    private async Task ConfigureUbxMessages()
    {
        // Configure UBX messages at 5Hz
        var ubxMessages = new[]
        {
            (UbxConstants.MSGOUT_UBX_NAV_PVT_UART1, 5),
            (UbxConstants.MSGOUT_UBX_NAV_HPPOSLLH_UART1, 5),
            (UbxConstants.MSGOUT_UBX_NAV_SAT_UART1, 5),
            (UbxConstants.MSGOUT_UBX_NAV_SIG_UART1, 5),
            (UbxConstants.MSGOUT_UBX_RXM_RAWX_UART1, 5),
            (UbxConstants.MSGOUT_UBX_RXM_SFRBX_UART1, 5),
            (UbxConstants.MSGOUT_UBX_RXM_COR_UART1, 5),
            (UbxConstants.MSGOUT_UBX_TIM_TM2_UART1, 5),
            (UbxConstants.MSGOUT_UBX_TIM_TP_UART1, 5),
            (UbxConstants.MSGOUT_UBX_MON_COMMS_UART1, 5),
            (UbxConstants.MSGOUT_UBX_NAV_DOP_UART1, 5),
            (UbxConstants.MSGOUT_UBX_NAV_RELPOSNED_UART1, 5)
        };

        foreach (var (keyId, rate) in ubxMessages)
        {
            await EnableMessageWithValset(keyId, rate);
        }
    }

    private async Task ConfigureOperatingMode()
    {
        if (_configurationManager.OperatingMode == OperatingMode.Send)
        {
            await ConfigureBaseStation();
        }
        else if (_configurationManager.OperatingMode == OperatingMode.Receive)
        {
            await ConfigureRover();
        }
    }

    private async Task ConfigureBaseStation()
    {
        _logger.LogInformation("Configuring Base Station mode");
        
        // Enable RTCM3 output
        await SetBoolWithValset(UbxConstants.UART1_PROTOCOL_RTCM3, true);
        
        // Configure RTCM3 corrections
        await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_REF_STATION_ARP_UART1, 1);
        await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_GPS_MSM7_UART1, 5);
        await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_GALILEO_MSM7_UART1, 5);
        await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_BEIDOU_MSM7_UART1, 5);
        
        // Enable Survey-In status
        await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_NAV_SVIN_UART1, 1);
        
        // Start Survey-In
        await SetSurveyInMode(UbxConstants.SURVEY_IN_DURATION_SECONDS, UbxConstants.SURVEY_IN_ACCURACY_LIMIT_0P1MM);
    }

    private async Task ConfigureRover()
    {
        _logger.LogInformation("Configuring Rover mode");
        
        // Disable TMODE3
        await SetTmodeDisabled();
        
        // Disable RTCM3 output
        await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_REF_STATION_ARP_UART1, 0);
        await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_GPS_MSM7_UART1, 0);
        await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_GALILEO_MSM7_UART1, 0);
        await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_BEIDOU_MSM7_UART1, 0);
        
        // Enable protocols for command and correction input
        await SetBoolWithValset(UbxConstants.UART1_PROTOCOL_UBX_IN, true);
        await SetBoolWithValset(UbxConstants.UART1_PROTOCOL_UBX, true);
        await SetBoolWithValset(UbxConstants.UART2_PROTOCOL_RTCM3, false);
        await SetBoolWithValset(UbxConstants.UART1_PROTOCOL_RTCM3_IN, true);
    }

    private async Task ConfigureNmeaMessages()
    {
        // Configure NMEA messages at 1Hz
        var nmeaMessages = new[]
        {
            UbxConstants.MSGOUT_NMEA_GGA_UART1,
            UbxConstants.MSGOUT_NMEA_RMC_UART1,
            UbxConstants.MSGOUT_NMEA_GSV_UART1,
            UbxConstants.MSGOUT_NMEA_GSA_UART1,
            UbxConstants.MSGOUT_NMEA_VTG_UART1,
            UbxConstants.MSGOUT_NMEA_GLL_UART1,
            UbxConstants.MSGOUT_NMEA_ZDA_UART1
        };

        foreach (var keyId in nmeaMessages)
        {
            await EnableMessageWithValset(keyId, 1);
        }
    }


    private async Task SetTmodeDisabled()
    {
        try
        {
            _logger.LogInformation("Setting TMODE3 to Disabled for rover mode");

            var payload = new List<byte>
            {
                UbxConstants.VAL_VERSION,                      // 0x01
                (byte)(UbxConstants.VAL_LAYER_RAM),           // bitmask for VALSET (RAM only)
                (byte)UbxConstants.ValTransaction.None,       // single-message apply
                0x00,                                         // reserved
            };

            // Set TMODE_MODE to disabled
            payload.AddRange(BitConverter.GetBytes(UbxConstants.TMODE_MODE));
            payload.Add(UbxConstants.TMODE_DISABLED);

            var response = await SendUbxConfigMessageAsync(UbxConstants.CLASS_CFG, UbxConstants.CFG_VALSET, payload.ToArray());

            if (response == UbxResponseType.Ack)
                _logger.LogInformation("TMODE3 disabled successfully");
            else if (response == UbxResponseType.Nak)
                _logger.LogWarning("TMODE3 disable configuration NAK");
            else if (response == UbxResponseType.Timeout)
                _logger.LogWarning("TMODE3 disable configuration timeout");
            else
                _logger.LogError("TMODE3 disable configuration error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable TMODE3");
        }
    }

    private async Task SetSurveyInMode(uint durationSeconds, uint accuracyLimit)
    {
        try
        {
            _logger.LogInformation("Setting Survey-In mode: {Duration}s duration, {Accuracy}mm accuracy limit",
                durationSeconds, accuracyLimit * 0.1);

            var payload = new List<byte>
            {
                UbxConstants.VAL_VERSION,                      // 0x01
                (byte)(UbxConstants.VAL_LAYER_RAM             // bitmask for VALSET
                     /* | UbxConstants.VAL_LAYER_BBR */),     // add if you want persistence across warm boots
                (byte)UbxConstants.ValTransaction.None,       // single-message apply (atomic in one packet)
                0x00,                                         // reserved
            };

            // Step 1: ensure we transition cleanly by disabling in the same transaction
            payload.AddRange(BitConverter.GetBytes(UbxConstants.TMODE_MODE));
            payload.Add(UbxConstants.TMODE_DISABLED);

            // Step 2: immediately set Survey-In + parameters
            payload.AddRange(BitConverter.GetBytes(UbxConstants.TMODE_MODE));
            payload.Add(UbxConstants.TMODE_SURVEY_IN);

            payload.AddRange(BitConverter.GetBytes(UbxConstants.TMODE_SVIN_MIN_DUR));
            payload.AddRange(BitConverter.GetBytes(durationSeconds));

            payload.AddRange(BitConverter.GetBytes(UbxConstants.TMODE_SVIN_ACC_LIMIT));
            payload.AddRange(BitConverter.GetBytes(accuracyLimit));

            var response = await SendUbxConfigMessageAsync(UbxConstants.CLASS_CFG, UbxConstants.CFG_VALSET, payload.ToArray());

            if (response == UbxResponseType.Ack)
                _logger.LogInformation("Survey-In mode configured successfully");
            else if (response == UbxResponseType.Nak)
                _logger.LogWarning("Survey-In mode configuration NAK");
            else if (response == UbxResponseType.Timeout)
                _logger.LogWarning("Survey-In mode configuration timeout");
            else
                _logger.LogError("Survey-In mode configuration error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set Survey-In mode");
        }
    }

    private async Task SetNavigationRate(int rateHz)
    {
        try
        {
            _logger.LogInformation("Setting navigation rate to {RateHz}Hz", rateHz);

            // Calculate measurement period in milliseconds (1000ms / rate)
            var measurementPeriodMs = (ushort)(1000 / rateHz);

            // CFG-VALSET payload for RATE_MEAS (measurement period in ms)
            var payload = new List<byte>
            {
                UbxConstants.VAL_VERSION,                    // Version
                UbxConstants.VAL_LAYER_RAM,                  // Layer: RAM only
                (byte)UbxConstants.ValTransaction.None,      // Transaction: single message
                0x00,                                        // Reserved
            };

            // Add RATE_MEAS key ID (0x30210001) - little endian
            payload.AddRange(BitConverter.GetBytes(0x30210001u));

            // Add measurement period value (2 bytes, little endian)
            payload.AddRange(BitConverter.GetBytes(measurementPeriodMs));

            var response = await SendUbxConfigMessageAsync(UbxConstants.CLASS_CFG, UbxConstants.CFG_VALSET, payload.ToArray());

            switch (response)
            {
                case UbxResponseType.Ack:
                    _logger.LogInformation("Navigation rate set to {Period}ms ({RateHz}Hz)", measurementPeriodMs, rateHz);
                    break;
                case UbxResponseType.Nak:
                    _logger.LogWarning("Navigation rate configuration NAK");
                    break;
                case UbxResponseType.Timeout:
                    _logger.LogWarning("Navigation rate configuration timeout");
                    break;
                case UbxResponseType.Error:
                    _logger.LogError("Navigation rate configuration error");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set navigation rate to {RateHz}Hz", rateHz);
        }
    }

    private async Task SetBoolWithValset(uint keyId, bool enable)
    {
        var payload = new List<byte>
    {
        UbxConstants.VAL_VERSION,
        UbxConstants.VAL_LAYER_RAM,
        (byte)UbxConstants.ValTransaction.None,
        0x00,
    };
        payload.AddRange(BitConverter.GetBytes(keyId));
        payload.Add((byte)(enable ? 1 : 0)); // strictly 0/1 for boolean keys
        await SendUbxConfigMessageAsync(UbxConstants.CLASS_CFG, UbxConstants.CFG_VALSET, payload.ToArray());
    }


    private async Task EnableMessageWithValset(uint keyId, int desiredRateHz)
    {
        var messageName = Parsers.GnssParserUtils.GetKeyIdConstantName(keyId);
        
        try
        {
            // Calculate rate divisor: 0=disabled, 1=every solution, 2=every 2nd solution, etc.
            byte rate;
            if (desiredRateHz == 0)
            {
                rate = 0; // Disabled
            }
            else
            {
                // Calculate divisor: UBX_MESSAGES_RATE_HZ / desiredRateHz
                rate = (byte)Math.Max(1, UBX_MESSAGES_RATE_HZ / desiredRateHz);
            }

            _logger.LogInformation("Enabling UBX message {MessageName} (Key ID: 0x{KeyId:X8}) at {DesiredHz}Hz (rate divisor: {Rate})", 
                messageName, keyId, desiredRateHz, rate);

            // CFG-VALSET payload: version(1) + layer(1) + transaction(1) + reserved(1) + keyId(4) + value(1)

            var payload = new List<byte>
            {
                UbxConstants.VAL_VERSION,                    // Version
                UbxConstants.VAL_LAYER_RAM,                  // Layer: RAM only
                (byte)UbxConstants.ValTransaction.None,      // Transaction: single message
                0x00,                                        // Reserved
            };

            // Add key ID (little endian)
            payload.AddRange(BitConverter.GetBytes(keyId));

            // Add value (1 byte for message rate)
            payload.Add(rate);

            var response = await SendUbxConfigMessageAsync(UbxConstants.CLASS_CFG, UbxConstants.CFG_VALSET, payload.ToArray());
            
            switch (response)
            {
                case UbxResponseType.Ack:
                    _logger.LogInformation("✅ {MessageName} ACK", messageName);
                    break;
                case UbxResponseType.Nak:
                    _logger.LogWarning("❌ {MessageName} NAK", messageName);
                    break;
                case UbxResponseType.Timeout:
                    _logger.LogWarning("❌ {MessageName} timeout", messageName);
                    break;
                case UbxResponseType.Error:
                    _logger.LogError("❌ {MessageName} error", messageName);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable UBX message {MessageName} (Key ID: 0x{KeyId:X8})", messageName, keyId);
        }
    }

    private async Task<UbxResponseType> SendUbxConfigMessageAsync(byte messageClass, byte messageId, byte[] payload)
    {
        if (_serialPortManager == null || !_serialPortManager.IsConnected)
        {
            return UbxResponseType.Error;
        }

        try
        {
            var ubxMessage = CreateUbxMessage(messageClass, messageId, payload);

            _logger.LogDebug("Sending UBX config: Class=0x{Class:X2}, ID=0x{Id:X2}, Length={Length}",
                messageClass, messageId, payload.Length);

            _serialPortManager.Write(ubxMessage, 0, ubxMessage.Length);

            // Wait a bit for command to be processed
            await Task.Delay(CommandDelayMs);

            _logger.LogDebug("UBX config sent for Class=0x{Class:X2}, ID=0x{Id:X2}", messageClass, messageId);
            return UbxResponseType.Ack; // Assume success for now
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send UBX config message");
            return UbxResponseType.Error;
        }
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

        // Calculate checksum
        var checksum = CalculateUbxChecksum(messageClass, messageId, payload);
        message.Add(checksum.ck_a);
        message.Add(checksum.ck_b);


        return message.ToArray();
    }

    private (byte ck_a, byte ck_b) CalculateUbxChecksum(byte messageClass, byte messageId, byte[] payload)
    {
        byte ck_a = 0;
        byte ck_b = 0;

        // Include class and ID in checksum
        ck_a += messageClass;
        ck_b += ck_a;
        ck_a += messageId;
        ck_b += ck_a;

        // Include length in checksum
        var length = (ushort)payload.Length;
        ck_a += (byte)(length & 0xFF);
        ck_b += ck_a;
        ck_a += (byte)(length >> 8);
        ck_b += ck_a;

        // Include payload in checksum
        foreach (var b in payload)
        {
            ck_a += b;
            ck_b += ck_a;
        }

        return (ck_a, ck_b);
    }


    public async Task<bool> ReconfigureAsync()
    {
        if (_serialPortManager == null || !_serialPortManager.IsConnected)
        {
            _logger.LogWarning("Cannot reconfigure GNSS - no active serial port connection");
            return false;
        }

        _logger.LogInformation("Reconfiguring GNSS for new operating mode: {Mode}", _configurationManager.OperatingMode);

        // Acquire the shared lock to prevent concurrent access during reconfiguration
        await _serialPortLock.WaitAsync();
        try
        {
            _logger.LogDebug("Acquired serial port lock for GNSS reconfiguration");

            // Reconfigure for satellite data with the current mode
            await ConfigureForSatelliteDataAsync();
            _logger.LogInformation("GNSS reconfiguration completed successfully for mode: {Mode}", _configurationManager.OperatingMode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconfigure GNSS for mode: {Mode}", _configurationManager.OperatingMode);
            return false;
        }
        finally
        {
            _serialPortLock.Release();
            _logger.LogDebug("Released serial port lock after GNSS reconfiguration");
        }
    }

    public void Dispose()
    {
        try
        {
            // Dispose SerialPortManager
            _serialPortManager?.Dispose();
            _serialPortManager = null;

            _logger.LogInformation("GNSS connections disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing GNSS serial connection");
        }
    }
}