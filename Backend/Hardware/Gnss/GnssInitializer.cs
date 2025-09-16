using System.IO.Ports;

namespace Backend.Hardware.Gnss;

public class GnssInitializer
{
    private readonly ILogger<GnssInitializer> _logger;
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
        115200, // Maximum common speed
        230400,
        460800
    };

    public GnssInitializer(ILogger<GnssInitializer> logger)
    {
        _logger = logger;
    }

    public async Task<bool> InitializeAsync(string portName = DefaultPortName)
    {
        _logger.LogInformation("Initializing GNSS on port {PortName}", portName);

        foreach (var baudRate in _baudRatesToScan)
        {
            _logger.LogInformation("Testing GNSS communication at {BaudRate} baud", baudRate);

            if (await TryBaudRateAsync(portName, baudRate))
            {
                _logger.LogInformation("GNSS initialized successfully on port {PortName} at {BaudRate} baud",
                    portName, _serialPort?.BaudRate ?? baudRate);
                return true;
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

            // Send standard NMEA query for position data
            var pollCommand = "$PUBX,00*33\r\n";
            _logger.LogDebug("Sending GNSS position query: {Command}", pollCommand.Trim());
            testPort.Write(pollCommand);

            // Wait for response
            var response = await WaitForGnssResponseAsync(testPort);

            if (!string.IsNullOrEmpty(response) && IsValidNmeaResponse(response))
            {
                _logger.LogDebug("GNSS response received at {BaudRate} baud: {Response}",
                    baudRate, response.Trim());

                // If we're not already at 460800, try to switch to it for optimal performance
                if (baudRate != 460800)
                {
                    _logger.LogInformation("Switching GNSS to 460800 baud for optimal performance");
                    if (await SwitchTo460800BaudAsync(testPort, (uint)baudRate, portName))
                    {
                        // Successfully switched - _serialPort is now set to 460800
                        testPort = null; // Prevent disposal
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("Baud rate switch to 460800 failed, keeping {BaudRate} baud", baudRate);
                        // Keep current connection as fallback
                        _serialPort = testPort;
                        testPort = null; // Prevent disposal
                        return true;
                    }
                }
                else
                {
                    // Already at optimal speed
                    _serialPort = testPort;
                    testPort = null; // Prevent disposal
                    return true;
                }
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
            await Task.Delay(2000);

            // Close current connection
            currentPort.Close();
            currentPort.Dispose();

            // Wait for device to reconfigure
            await Task.Delay(2000);

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
            testPort.Write(pollCommand);

            var response = await WaitForGnssResponseAsync(testPort);

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

            await Task.Delay(50);
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