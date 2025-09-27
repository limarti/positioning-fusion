using System.IO.Ports;
using Backend.Hardware.Common;

namespace Backend.Hardware.Imu;

public class ImuInitializer
{
    private readonly ILogger<ImuInitializer> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private SerialPortManager? _serialPortManager;
    
    private const string DefaultPortName = "/dev/ttyAMA2";
    private const int DefaultBaudRate = 115200;
    private const int InitializationTimeoutMs = 3000;  // 3-second check for IMU data

    public ImuInitializer(ILogger<ImuInitializer> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<bool> InitializeAsync(string portName = DefaultPortName, int baudRate = DefaultBaudRate)
    {
        try
        {
            _logger.LogInformation("Initializing IM19 IMU on port {PortName} at {BaudRate} baud", portName, baudRate);

            // Create SerialPortManager with IMU configuration
            _serialPortManager = new SerialPortManager(
                "IMU",
                portName,
                baudRate,
                _loggerFactory.CreateLogger<SerialPortManager>(),
                100, // pollingIntervalMs
                1024, // readBufferSize
                10240 // maxBufferSize
                // Using defaults for rateUpdateIntervalMs (1000), parity (None), dataBits (8), stopBits (One)
            );

            // Start SerialPortManager (it will create and open the serial port)
            await _serialPortManager.StartAsync();
            _logger.LogInformation("SerialPortManager started for IMU on {PortName}", portName);

            await Task.Delay(500);

            // Configure IMU and test for data within 3 seconds
            await ConfigureImuOutputAsync();

            if (await VerifyImuCommunicationAsync())
            {
                _logger.LogInformation("IM19 IMU initialized successfully - data received within 3 seconds, keeping SerialPortManager ready");
                return true;
            }
            else
            {
                _logger.LogWarning("IM19 IMU not detected - no data received within 3 seconds, disposing resources");
                _serialPortManager?.Dispose();
                _serialPortManager = null;
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize IM19 IMU on port {PortName}", portName);
            _serialPortManager?.Dispose();
            _serialPortManager = null;
            return false;
        }
    }

    private async Task<bool> VerifyImuCommunicationAsync()
    {
        if (_serialPortManager == null || !_serialPortManager.IsConnected)
            return false;

        _logger.LogInformation("Checking for IMU data within 3 seconds...");

        var dataReceivedEvent = new TaskCompletionSource<bool>();

        // Subscribe to data events
        _serialPortManager.DataReceived += (sender, data) =>
        {
            if (data.Length > 0)
            {
                dataReceivedEvent.TrySetResult(true);
            }
        };

        // Wait for data or timeout (SerialPortManager is already started)
        using var cts = new CancellationTokenSource(InitializationTimeoutMs);
        try
        {
            await dataReceivedEvent.Task.WaitAsync(cts.Token);
            _logger.LogInformation("IM19 IMU detected - data received within 3 seconds");
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("IM19 IMU not detected - no data received within 3 seconds");
            return false;
        }
        // Note: Don't stop SerialPortManager here - it will be used by the service
    }

    private async Task ConfigureImuOutputAsync()
    {
        if (_serialPortManager == null || !_serialPortManager.IsConnected)
            return;

        _logger.LogDebug("Configuring IM19 IMU for continuous data output...");

        const string atCommand = "AT+MEMS_OUTPUT=UART1,ON";
        _logger.LogDebug("Sending AT command: {Command}", atCommand);

        _serialPortManager.DiscardInBuffer();
        _serialPortManager.WriteLine(atCommand);
        var response = await WaitForAtResponseAsync();

        if (response.Contains("OK"))
        {
            _logger.LogInformation("IM19 IMU MEMS output enabled successfully");
        }
        else if (response.Contains("ERROR"))
        {
            _logger.LogError("IM19 IMU AT command failed with response: {Response}", response);
            throw new InvalidOperationException($"AT command failed: {response}");
        }
        else
        {
            _logger.LogWarning("IM19 IMU AT command response unclear: {Response}", response);
        }

        _logger.LogDebug("IM19 IMU configuration completed");
    }

    private async Task<string> WaitForAtResponseAsync(int timeoutMs = 3000)
    {
        var response = string.Empty;
        var endTime = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < endTime)
        {
            try
            {
                var data = _serialPortManager?.ReadExisting();
                if (!string.IsNullOrEmpty(data))
                {
                    response += data;

                    if (response.Contains("OK") || response.Contains("ERROR"))
                    {
                        _logger.LogDebug("AT command response received: {Response}", response.Trim());
                        return response.Trim();
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // SerialPort not available, continue waiting
            }

            await Task.Delay(50);
        }

        _logger.LogWarning("AT command response timeout after {TimeoutMs}ms", timeoutMs);
        return response.Trim();
    }

    public SerialPortManager? GetSerialPortManager()
    {
        return _serialPortManager;
    }

    public void Dispose()
    {
        try
        {
            _serialPortManager?.Dispose();
            _serialPortManager = null;
            _logger.LogInformation("IM19 IMU SerialPortManager disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing IM19 IMU SerialPortManager");
        }
    }
}