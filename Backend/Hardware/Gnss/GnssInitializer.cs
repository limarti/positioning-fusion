using System.IO.Ports;
using Backend.Configuration;

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
    private SerialPort? _serialPort;

    private const string DefaultPortName = "/dev/ttyAMA0";
    private const Parity DefaultParity = Parity.None;
    private const int DefaultDataBits = 8;
    private const StopBits DefaultStopBits = StopBits.One;
    private const int ResponseTimeoutMs = 3000;
    private const int PortStabilizationDelayMs = 500;
    private const int CommandDelayMs = 200;
    private const int BaudRateDelayMs = 3000;

    // ZED-X20P specific baud rates - default is 38400
    private readonly int[] _baudRatesToScan = new int[]
    {
        //the only ones we are using. default, and ours
        460800,
        38400,

        //4800,   // Older devices
        //9600,   // Most common default
        //19200,  // Higher speed
        //57600,  // Very high speed
        //115200, // Maximum common speed
        //230400,
    };

    public GnssInitializer(ILogger<GnssInitializer> logger)
    {
        _logger = logger;
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
                        portName, _serialPort?.BaudRate ?? baudRate);

                    // Configure GNSS for satellite data output
                    await ConfigureForSatelliteDataAsync();

                    return true;
                }

                // Wait between baud rate attempts (except after the last one)
                if (i < _baudRatesToScan.Length - 1)
                {
                    _logger.LogDebug("Waiting 3 seconds before trying next baud rate...");
                    await Task.Delay(3000);
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
        return false;
    }

    private SerialPort CreateSerialPort(string portName, int baudRate)
    {
        return new SerialPort(portName, baudRate, DefaultParity, DefaultDataBits, DefaultStopBits)
        {
            ReadTimeout = ResponseTimeoutMs,
            WriteTimeout = ResponseTimeoutMs,
            RtsEnable = true,
            DtrEnable = true
        };
    }

    private async Task<string> TestGnssCommunicationAsync(SerialPort serialPort)
    {
        // Allow port to stabilize
        await Task.Delay(PortStabilizationDelayMs);

        // Clear any existing data
        serialPort.DiscardInBuffer();
        serialPort.DiscardOutBuffer();

        // Send standard NMEA query for position data
        var pollCommand = "$PUBX,00*33\r\n";
        _logger.LogDebug("Sending GNSS position query: {Command}", pollCommand.Trim());
        serialPort.Write(pollCommand);

        // Wait before checking for response to allow device to process
        await Task.Delay(CommandDelayMs);

        // Wait for response
        return await WaitForGnssResponseAsync(serialPort);
    }

    private async Task<bool> TryBaudRateAsync(string portName, int baudRate)
    {
        SerialPort? testPort = null;

        try
        {
            testPort = CreateSerialPort(portName, baudRate);
            testPort.Open();
            _logger.LogDebug("Serial port {PortName} opened at {BaudRate} baud", portName, baudRate);

            // Test GNSS communication
            var response = await TestGnssCommunicationAsync(testPort);

            if (!string.IsNullOrEmpty(response) && IsValidNmeaResponse(response))
            {
                _logger.LogDebug("GNSS response received at {BaudRate} baud: {Response}",
                    baudRate, response.Trim());

                // Try to switch to optimal speed (460800) if not already there
                if (baudRate != 460800 && await TrySwitchToOptimalBaudRate(testPort, baudRate, portName))
                {
                    // Successfully switched - _serialPort is now set to 460800
                    testPort = null; // Prevent disposal
                    return true;
                }

                // Use current baud rate (either 460800 already, or switch failed)
                _serialPort = testPort;
                testPort = null; // Prevent disposal
                return true;
            }
            else
            {
                _logger.LogDebug("No valid response received at {BaudRate} baud", baudRate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to test GNSS at {BaudRate} baud", baudRate);
        }
        finally
        {
            try
            {
                testPort?.Close();
                testPort?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing test port at {BaudRate} baud", baudRate);
            }
        }

        return false;
    }

    private async Task<bool> TrySwitchToOptimalBaudRate(SerialPort currentPort, int currentBaudRate, string portName)
    {
        _logger.LogInformation("Switching GNSS from {CurrentBaud} to 460800 baud", currentBaudRate);
        
        if (await SwitchTo460800BaudAsync(currentPort, (uint)currentBaudRate, portName))
        {
            return true;
        }
        
        _logger.LogWarning("Baud rate switch to 460800 failed, keeping {BaudRate} baud", currentBaudRate);
        return false;
    }

    private async Task<bool> SwitchTo460800BaudAsync(SerialPort currentPort, uint currentBaudRate, string portName)
    {
        try
        {
            _logger.LogDebug("Switching GNSS from {CurrentBaud} to 460800 baud using NMEA PUBX command", currentBaudRate);

            // Use NMEA PUBX,41 command to change baud rate (reliable for u-blox modules)
            var pubxBaudCommand = "$PUBX,41,1,0003,0003,460800,0";
            var checksum = CalculateNmeaChecksum(pubxBaudCommand.Substring(1));
            pubxBaudCommand += $"*{checksum:X2}\r\n";

            _logger.LogDebug("Sending PUBX baud command: {Command}", pubxBaudCommand.Trim());
            currentPort.Write(pubxBaudCommand);

            // Wait for command to be processed
            await Task.Delay(BaudRateDelayMs);

            // Close current connection
            currentPort.Close();
            currentPort.Dispose();

            // Wait for device to reconfigure
            await Task.Delay(BaudRateDelayMs);

            // Test reconnection at 460800 baud
            return await TestAndSetConnection(portName, 460800);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch baud rate to 460800");
            return false;
        }
    }

    private async Task<bool> TestAndSetConnection(string portName, int baudRate)
    {
        SerialPort? testPort = null;

        try
        {
            testPort = CreateSerialPort(portName, baudRate);
            testPort.Open();

            var response = await TestGnssCommunicationAsync(testPort);

            if (!string.IsNullOrEmpty(response) && IsValidNmeaResponse(response))
            {
                _logger.LogDebug("Communication confirmed at {BaudRate} baud", baudRate);
                _serialPort = testPort;
                testPort = null; // Prevent disposal
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error testing {BaudRate} baud", baudRate);
            return false;
        }
        finally
        {
            try
            {
                testPort?.Close();
                testPort?.Dispose();
            }
            catch { }
        }
    }

    private async Task<string> WaitForGnssResponseAsync(SerialPort serialPort)
    {
        var response = string.Empty;
        var endTime = DateTime.UtcNow.AddMilliseconds(ResponseTimeoutMs);

        while (DateTime.UtcNow < endTime)
        {
            try
            {
                if (serialPort.BytesToRead > 0)
                {
                    var data = serialPort.ReadExisting();
                    response += data;

                    // Check if we have at least one complete NMEA sentence
                    if (response.Contains('\n'))
                    {
                        _logger.LogDebug("GNSS response data received: {Data}", response.Trim());
                        return response.Trim();
                    }
                }
            }
            catch (TimeoutException)
            {
                // Expected when no data available
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error reading GNSS response");
                break;
            }

            await Task.Delay(100);
        }

        return response.Trim();
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

    public SerialPort? GetSerialPort()
    {
        return _serialPort;
    }

    const int UBX_MESSAGES_RATE_HZ = 10;    //1, 5, 10 or 20
    private async Task ConfigureForSatelliteDataAsync()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            _logger.LogWarning("Cannot configure GNSS - serial port not available");
            return;
        }

        _logger.LogInformation("Configuring GNSS for satellite data output (runtime only)");

        try
        {
            _logger.LogInformation("ZED-X20P: Using CFG-VALSET for modern UBX configuration");
            _logger.LogInformation("Corrections Mode: {Mode}, GNSS Rate: {Rate}Hz", SystemConfiguration.CorrectionsOperation, UBX_MESSAGES_RATE_HZ);

            await SetNavigationRate(UBX_MESSAGES_RATE_HZ);

            // Enable messages at full rate (10Hz) - every navigation solution
            await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_NAV_PVT_UART1, 10);
            await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_NAV_SAT_UART1, 10);
            await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_NAV_SIG_UART1, 10);
            await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_RXM_RAWX_UART1, 10);
            await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_RXM_SFRBX_UART1, 10);
            await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_RXM_COR_UART1, 10);
            await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_TIM_TM2_UART1, 10);
            await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_TIM_TP_UART1, 10);
            await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_MON_COMMS_UART1, 10);
            await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_NAV_DOP_UART1, 10);

            if (SystemConfiguration.CorrectionsOperation == SystemConfiguration.CorrectionsMode.Send)
            {
                _logger.LogInformation("Configuring Base Station mode - Survey-In with RTCM3 output");

                //await EnableMessageWithValset(UbxConstants.UART1_PROTOCOL_UBX, 1);
                //await EnableMessageWithValset(UbxConstants.UART1_PROTOCOL_NMEA, 1);
                await SetBoolWithValset(UbxConstants.UART1_PROTOCOL_RTCM3, true);

                // Configure RTCM3 message rates
                await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_REF_STATION_ARP_UART1, 1);  // 1Hz for reference station position

                //Corrections
                await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_GPS_MSM7_UART1, 5);
                await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_GALILEO_MSM7_UART1, 5);
                await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_BEIDOU_MSM7_UART1, 5);

                // unsupported as output:
                // await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_GLONASS_MSM7_UART1, 10);
                // await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_GLONASS_CODE_PHASE_BIASES_UART1, 1);

                // Enable Survey-In status monitoring
                await EnableMessageWithValset(UbxConstants.MSGOUT_UBX_NAV_SVIN_UART1, 1);

                // Start Survey-In mode after RTCM3 configuration
                await SetSurveyInMode(UbxConstants.SURVEY_IN_DURATION_SECONDS, UbxConstants.SURVEY_IN_ACCURACY_LIMIT_0P1MM);

                _logger.LogInformation("Base Station mode configuration completed");
            }
            else if (SystemConfiguration.CorrectionsOperation == SystemConfiguration.CorrectionsMode.Receive)
            {
                _logger.LogInformation("Configuring Rover mode - TMODE3 disabled, RTCM3 reception on UART1");

                // Ensure TMODE3 = Disabled (rover must not be in survey-in/fixed mode)
                await SetTmodeDisabled();

                // Disable generating RTCM3 messages (rover shouldn't generate them)
                await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_REF_STATION_ARP_UART1, 0);
                await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_GPS_MSM7_UART1, 0);
                await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_GALILEO_MSM7_UART1, 0);
                await EnableMessageWithValset(UbxConstants.MSGOUT_RTCM3_BEIDOU_MSM7_UART1, 0);

                // Ensure UBX input/output enabled on UART1 (so we can still send VALSET commands)
                await SetBoolWithValset(UbxConstants.UART1_PROTOCOL_UBX_IN, true);
                await SetBoolWithValset(UbxConstants.UART1_PROTOCOL_UBX, true);
                
                // Enable RTCM3 input on UART1 for corrections reception, disable on UART2
                await SetBoolWithValset(UbxConstants.UART2_PROTOCOL_RTCM3, false);
                await SetBoolWithValset(UbxConstants.UART1_PROTOCOL_RTCM3_IN, true);

                _logger.LogInformation("Rover mode configuration completed - TMODE3 disabled, ready for corrections");
            }

            await EnableMessageWithValset(UbxConstants.MSGOUT_NMEA_GGA_UART1, 1);
            await EnableMessageWithValset(UbxConstants.MSGOUT_NMEA_RMC_UART1, 1);
            await EnableMessageWithValset(UbxConstants.MSGOUT_NMEA_GSV_UART1, 1);
            await EnableMessageWithValset(UbxConstants.MSGOUT_NMEA_GSA_UART1, 1);
            await EnableMessageWithValset(UbxConstants.MSGOUT_NMEA_VTG_UART1, 1);
            await EnableMessageWithValset(UbxConstants.MSGOUT_NMEA_GLL_UART1, 1);
            await EnableMessageWithValset(UbxConstants.MSGOUT_NMEA_ZDA_UART1, 1);
            
            _logger.LogInformation("NMEA sentence configuration completed using CFG-VALSET");
            _logger.LogInformation("ZED-X20P UBX configuration completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure GNSS for satellite data");
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
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            return UbxResponseType.Error;
        }

        try
        {
            var ubxMessage = CreateUbxMessage(messageClass, messageId, payload);

            _logger.LogDebug("Sending UBX config: Class=0x{Class:X2}, ID=0x{Id:X2}, Length={Length}",
                messageClass, messageId, payload.Length);

            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();
            _serialPort.Write(ubxMessage, 0, ubxMessage.Length);

            // Wait for and parse ACK/NAK response
            var response = await WaitForUbxAckResponse(messageClass, messageId);
            
            if (response.HasValue)
            {
                if (response.Value)
                {
                    _logger.LogDebug("UBX config ACK received for Class=0x{Class:X2}, ID=0x{Id:X2}", messageClass, messageId);
                    return UbxResponseType.Ack;
                }
                else
                {
                    _logger.LogWarning("UBX config NAK received for Class=0x{Class:X2}, ID=0x{Id:X2}", messageClass, messageId);
                    return UbxResponseType.Nak;
                }
            }
            else
            {
                _logger.LogWarning("No UBX response received for Class=0x{Class:X2}, ID=0x{Id:X2} (timeout)", messageClass, messageId);
                return UbxResponseType.Timeout;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send UBX config message");
            return UbxResponseType.Error;
        }
    }

    private async Task<bool?> WaitForUbxAckResponse(byte expectedClass, byte expectedId)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            return null;
        }

        const int timeoutMs = 3000;
        var buffer = new List<byte>();
        var endTime = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < endTime)
        {
            try
            {
                if (_serialPort.BytesToRead > 0)
                {
                    var data = new byte[_serialPort.BytesToRead];
                    var bytesRead = _serialPort.Read(data, 0, data.Length);
                    buffer.AddRange(data.Take(bytesRead));

                    // Look for UBX ACK/NAK messages in the buffer
                    var ackResult = ParseUbxAckFromBuffer(buffer, expectedClass, expectedId);
                    if (ackResult.HasValue)
                    {
                        _logger.LogDebug("UBX response parsed: {Response} for Class=0x{Class:X2}, ID=0x{Id:X2}", 
                            ackResult.Value ? "ACK" : "NAK", expectedClass, expectedId);
                        return ackResult.Value;
                    }
                }

                await Task.Delay(50); // Small delay to avoid busy waiting
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error reading UBX response");
                break;
            }
        }

        _logger.LogDebug("Timeout waiting for UBX response for Class=0x{Class:X2}, ID=0x{Id:X2}", expectedClass, expectedId);
        return null;
    }

    private bool? ParseUbxAckFromBuffer(List<byte> buffer, byte expectedClass, byte expectedId)
    {
        // Look for UBX sync pattern (0xB5 0x62)
        for (int i = 0; i < buffer.Count - 7; i++) // Minimum UBX message is 8 bytes
        {
            if (buffer[i] == UbxConstants.SYNC_CHAR_1 && buffer[i + 1] == UbxConstants.SYNC_CHAR_2)
            {
                // Check if we have enough bytes for a complete ACK/NAK message
                if (i + 9 >= buffer.Count) continue; // ACK/NAK is 10 bytes total

                var messageClass = buffer[i + 2];
                var messageId = buffer[i + 3];
                var length = BitConverter.ToUInt16(new byte[] { buffer[i + 4], buffer[i + 5] }, 0);

                // Check for ACK class (0x05)
                if (messageClass == UbxConstants.CLASS_ACK && length == 2)
                {
                    var ackClass = buffer[i + 6];
                    var ackId = buffer[i + 7];

                    // Verify this ACK/NAK is for our message
                    if (ackClass == expectedClass && ackId == expectedId)
                    {
                        if (messageId == UbxConstants.ACK_ACK)
                        {
                            // Remove processed bytes from buffer
                            buffer.RemoveRange(0, i + 10);
                            return true; // ACK
                        }
                        else if (messageId == UbxConstants.ACK_NAK)
                        {
                            // Remove processed bytes from buffer
                            buffer.RemoveRange(0, i + 10);
                            return false; // NAK
                        }
                    }
                }

                // Remove invalid/processed sync pattern
                buffer.RemoveRange(0, i + 2);
                i = -1; // Reset search after buffer modification
            }
        }

        return null; // No complete ACK/NAK found yet
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


    public void Dispose()
    {
        try
        {
            _serialPort?.Close();
            _serialPort?.Dispose();
            _serialPort = null;
            _logger.LogInformation("GNSS serial connection disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing GNSS serial connection");
        }
    }
}