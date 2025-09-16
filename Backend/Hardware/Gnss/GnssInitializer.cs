using System.IO.Ports;

namespace Backend.Hardware.Gnss;

public class GnssInitializer
{
    private readonly ILogger<GnssInitializer> _logger;
    private readonly UbxCommunication _ubxCommunication;
    private SerialPort? _serialPort;

    private const string DefaultPortName = "/dev/ttyAMA0";
    private const Parity DefaultParity = Parity.None;
    private const int DefaultDataBits = 8;
    private const StopBits DefaultStopBits = StopBits.One;
    private const int ResponseTimeoutMs = 1000;

    // Common GNSS baud rates to scan
    private readonly int[] _baudRatesToScan = new int[]
    {
        9600,   // Most common default
        4800,   // Older devices
        19200,  // Higher speed
        38400,  // High speed
        57600,  // Very high speed
        115200,  // Maximum common speed
        230400,
        460800
    };

    public GnssInitializer(ILogger<GnssInitializer> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _ubxCommunication = new UbxCommunication(loggerFactory.CreateLogger<UbxCommunication>());
    }

    public async Task<bool> InitializeAsync(string portName = DefaultPortName)
    {
        _logger.LogInformation("Initializing GNSS on port {PortName}", portName);

        foreach (var baudRate in _baudRatesToScan)
        {
            _logger.LogInformation("Testing GNSS communication at {BaudRate} baud...", baudRate);
            Console.WriteLine($"Testing GNSS at {baudRate} baud...");

            if (await TryBaudRateAsync(portName, baudRate))
            {
                _logger.LogInformation("GNSS initialized successfully on port {PortName} at {BaudRate} baud",
                    portName, baudRate);
                Console.WriteLine($"✓ GNSS connected successfully at {baudRate} baud");
                return true;
            }
            else
            {
                Console.WriteLine($"✗ No response at {baudRate} baud");
            }
        }

        _logger.LogError("Failed to initialize GNSS on port {PortName} - no valid baud rate found", portName);
        return false;
    }

    private async Task<bool> TryBaudRateAsync(string portName, int baudRate)
    {
        SerialPort? testPort = null;

        try
        {
            testPort = new SerialPort(portName, baudRate, DefaultParity, DefaultDataBits, DefaultStopBits)
            {
                ReadTimeout = ResponseTimeoutMs,
                WriteTimeout = ResponseTimeoutMs,
                RtsEnable = true,
                DtrEnable = true
            };

            testPort.Open();
            _logger.LogDebug("Serial port {PortName} opened at {BaudRate} baud", portName, baudRate);

            // Allow port to stabilize
            await Task.Delay(100);

            // Clear any existing data
            testPort.DiscardInBuffer();
            testPort.DiscardOutBuffer();

            // Send standard NMEA query for recommended minimum data
            // This should return either RMC data or an error - both indicate communication
            var pollCommand = "$PUBX,00*33\r\n";
            _logger.LogDebug("Sending GNSS position query: {Command}", pollCommand.Trim());
            Console.WriteLine($"  → Sending position query: {pollCommand.Trim()}");

            testPort.Write(pollCommand);

            // Wait for response
            var response = await WaitForGnssResponseAsync(testPort);

            if (!string.IsNullOrEmpty(response))
            {
                _logger.LogDebug("GNSS response received at {BaudRate} baud: {Response}",
                    baudRate, response.Trim());
                Console.WriteLine($"  ← Response: {response.Trim()}");

                // If we got any NMEA-like response, consider it successful
                if (IsValidNmeaResponse(response))
                {
                    Console.WriteLine($"  ✓ Valid NMEA response detected");

                    // If we're not already at 460800, try to switch to it using UBX
                    if (baudRate != 460800)
                    {
                        Console.WriteLine($"  → Attempting UBX baud rate switch to 460800...");
                        if (await SwitchBaudRateUbxAsync(testPort, baudRate, 460800, portName))
                        {
                            // Successfully switched and verified - _serialPort is now set
                            testPort = null; // Prevent disposal
                            return true;
                        }
                        else
                        {
                            Console.WriteLine($"  ✗ UBX baud rate switch failed, keeping {baudRate} baud");
                            // Keep current connection as fallback
                            _serialPort = testPort;
                            testPort = null; // Prevent disposal
                            return true;
                        }
                    }
                    else
                    {
                        // Already at 460800
                        _serialPort = testPort;
                        testPort = null; // Prevent disposal
                        return true;
                    }
                }
                else
                {
                    Console.WriteLine($"  ✗ Invalid response format");
                }
            }
            else
            {
                _logger.LogDebug("No response received at {BaudRate} baud", baudRate);
                Console.WriteLine($"  ✗ No response received");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to test GNSS at {BaudRate} baud", baudRate);
            Console.WriteLine($"  ✗ Error: {ex.Message}");
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

    private async Task<bool> SwitchBaudRateUbxAsync(SerialPort currentPort, int currentBaudRate, uint targetBaudRate, string portName)
    {
        try
        {
            Console.WriteLine($"    → Using UBX protocol to switch from {currentBaudRate} to {targetBaudRate} baud");

            // First try to enable UBX protocol (many u-blox modules start with only NMEA enabled)
            Console.WriteLine($"    → Enabling UBX protocol on u-blox module...");
            var ubxEnabled = await _ubxCommunication.EnableUbxProtocolAsync(currentPort, 1, (uint)currentBaudRate);

            if (!ubxEnabled)
            {
                Console.WriteLine($"    → UBX enable command failed, trying direct UBX communication...");
            }
            else
            {
                Console.WriteLine($"    ✓ UBX protocol enabled");
            }

            // First try to get module information with MON-VER to understand what we're dealing with
            Console.WriteLine($"    → Getting module information with MON-VER...");
            var monVerPayload = _ubxCommunication.CreateMonVerCommand();
            var monVerWorked = await _ubxCommunication.SendUbxCommandAsync(currentPort, 0x0A, 0x04, monVerPayload);

            if (monVerWorked)
            {
                Console.WriteLine($"    ✓ MON-VER command accepted - this confirms u-blox UBX protocol");
            }
            else
            {
                Console.WriteLine($"    ✗ MON-VER command rejected - unusual for u-blox modules");
            }

            // Since CFG-PRT commands are consistently NAKed, this might be a newer u-blox generation
            // that requires different CFG-PRT format or has restrictions
            Console.WriteLine($"    → Trying alternative approaches for u-blox baud rate change...");

            // Try using NMEA PUBX commands instead of UBX binary for baud rate change
            Console.WriteLine($"    → Attempting NMEA PUBX baud rate command...");
            var pubxBaudCommand = $"$PUBX,41,1,0003,0003,{targetBaudRate},0";
            var checksum = CalculateNmeaChecksum(pubxBaudCommand.Substring(1));
            pubxBaudCommand += $"*{checksum:X2}\r\n";

            Console.WriteLine($"    → Sending PUBX baud command: {pubxBaudCommand.Trim()}");
            currentPort.Write(pubxBaudCommand);

            // Wait for command to be processed
            await Task.Delay(2000);

            // Close current connection
            Console.WriteLine($"    → Closing connection at {currentBaudRate} baud");
            currentPort.Close();
            currentPort.Dispose();

            // Wait for device to reconfigure
            Console.WriteLine($"    → Waiting 2000ms for device reconfiguration...");
            await Task.Delay(2000);

            // Try to reconnect at new baud rate
            Console.WriteLine($"    → Attempting reconnection at {targetBaudRate} baud...");
            var success = await TestBaudRateAndSetConnection(portName, (int)targetBaudRate);

            if (success)
            {
                return true;
            }
            else
            {
                Console.WriteLine($"    ✗ PUBX baud rate change failed");
            }

            // Fallback: try the original approaches if polling failed
            Console.WriteLine($"    → CFG-PRT polling failed, trying fallback approaches...");

            // Send CFG-PRT command to change baud rate - try alternative format first
            Console.WriteLine($"    → Sending UBX CFG-PRT (baud-only) command...");
            var cfgPrtPayload = _ubxCommunication.CreateCfgPrtBaudOnlyCommand(1, targetBaudRate); // Port 1 = UART1
            var ackReceived2 = await _ubxCommunication.SendUbxCommandAsync(currentPort, 0x06, 0x00, cfgPrtPayload);

            if (!ackReceived2)
            {
                Console.WriteLine($"    → Baud-only command failed, trying full CFG-PRT command...");
                cfgPrtPayload = _ubxCommunication.CreateCfgPrtCommand(1, targetBaudRate);
                ackReceived2 = await _ubxCommunication.SendUbxCommandAsync(currentPort, 0x06, 0x00, cfgPrtPayload);

                if (!ackReceived2)
                {
                    Console.WriteLine($"    ✗ All CFG-PRT command formats failed");
                    return false;
                }
            }

            Console.WriteLine($"    ✓ CFG-PRT command acknowledged");

            // Close current connection
            Console.WriteLine($"    → Closing connection at {currentBaudRate} baud");
            currentPort.Close();
            currentPort.Dispose();

            // Wait for device to reconfigure
            Console.WriteLine($"    → Waiting 2000ms for device reconfiguration...");
            await Task.Delay(2000);

            // Try to reconnect at new baud rate
            Console.WriteLine($"    → Attempting reconnection at {targetBaudRate} baud...");
            return await TestBaudRateAndSetConnection(portName, (int)targetBaudRate);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ✗ Error in UBX baud rate switch: {ex.Message}");
            _logger.LogError(ex, "Failed to switch baud rate using UBX protocol");
            return false;
        }
    }


    private async Task<bool> TestBaudRateAndSetConnection(string portName, int baudRate)
    {
        SerialPort? testPort = null;

        try
        {
            testPort = new SerialPort(portName, baudRate, DefaultParity, DefaultDataBits, DefaultStopBits)
            {
                ReadTimeout = ResponseTimeoutMs,
                WriteTimeout = ResponseTimeoutMs,
                RtsEnable = true,
                DtrEnable = true
            };

            testPort.Open();
            await Task.Delay(100);

            testPort.DiscardInBuffer();
            testPort.DiscardOutBuffer();

            var pollCommand = "$PUBX,00*33\r\n";
            Console.WriteLine($"    → Testing communication at {baudRate} baud");
            testPort.Write(pollCommand);

            var response = await WaitForGnssResponseAsync(testPort);

            if (!string.IsNullOrEmpty(response) && IsValidNmeaResponse(response))
            {
                Console.WriteLine($"    ✓ Communication confirmed at {baudRate} baud");
                Console.WriteLine($"    ✓ Actual response received: {response.Trim()}");
                Console.WriteLine($"    ✓ Setting _serialPort to {baudRate} baud connection");

                _serialPort = testPort;
                testPort = null; // Prevent disposal

                // Additional verification - try reading a few more messages
                Console.WriteLine($"    → Reading additional messages to verify {baudRate} baud stability...");
                for (int i = 0; i < 3; i++)
                {
                    await Task.Delay(500);
                    var additionalResponse = await WaitForGnssResponseAsync(_serialPort);
                    if (!string.IsNullOrEmpty(additionalResponse))
                    {
                        Console.WriteLine($"    ← Message {i+1}: {additionalResponse.Trim()}");
                    }
                }

                return true;
            }
            else
            {
                Console.WriteLine($"    ✗ No valid response at {baudRate} baud");
                Console.WriteLine($"    ✗ Response was: '{response}'");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ✗ Error testing {baudRate} baud: {ex.Message}");
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

            await Task.Delay(50);
        }

        return response.Trim();
    }

    private static bool IsValidNmeaResponse(string response)
    {
        // Check for specific PUBX,00 position response format
        // Expected: $PUBX,00,<time>,<lat>,<N/S>,<lon>,<E/W>,<alt>,<nav_stat>,<h_acc>,<v_acc>,<sog>,<cog>,<v_vel>,<age>,<hdop>,<vdop>,<tdop>,<num_sat>,<reserved>,<dead_reckoning>*hh
        if (response.Contains("$PUBX,00"))
        {
            return true;
        }

        // Also accept standard NMEA sentences as valid communication
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

        // Reject everything else (including garbage like "???%????")
        return false;
    }

    public SerialPort? GetSerialPort()
    {
        return _serialPort;
    }

    public void LogCurrentConnectionStatus()
    {
        if (_serialPort != null && _serialPort.IsOpen)
        {
            Console.WriteLine($"🔍 GNSS Connection Status:");
            Console.WriteLine($"   - Port: {_serialPort.PortName}");
            Console.WriteLine($"   - Baud Rate: {_serialPort.BaudRate}");
            Console.WriteLine($"   - Is Open: {_serialPort.IsOpen}");
            Console.WriteLine($"   - Data Bits: {_serialPort.DataBits}");
            Console.WriteLine($"   - Parity: {_serialPort.Parity}");
            Console.WriteLine($"   - Stop Bits: {_serialPort.StopBits}");
            _logger.LogInformation("GNSS final connection: {PortName} at {BaudRate} baud",
                _serialPort.PortName, _serialPort.BaudRate);
        }
        else
        {
            Console.WriteLine($"🔍 GNSS Connection Status: No active connection");
            _logger.LogWarning("GNSS connection status: No active serial port");
        }
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