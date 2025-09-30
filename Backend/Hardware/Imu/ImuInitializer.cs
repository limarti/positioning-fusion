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

    public bool IsInitialized { get; private set; } = false;

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

            // Create SerialPortManager with IMU configuration - use polling-only mode to reduce CPU
            _serialPortManager = new SerialPortManager(
                "IMU",
                portName,
                baudRate,
                _loggerFactory.CreateLogger<SerialPortManager>(),
                pollingIntervalMs: 50,   // Poll every 50ms (20Hz) - lower frequency = lower CPU
                readBufferSize: 1024,
                maxBufferSize: 10240,
                rateUpdateIntervalMs: 1000,
                parity: System.IO.Ports.Parity.None,
                dataBits: 8,
                stopBits: System.IO.Ports.StopBits.One,
                minBatchSize: 100,       // Increased batch size for 50ms polling
                batchTimeoutMs: 50,      // Match polling interval
                forcePollingMode: true   // Force polling mode to avoid 330 events/sec CPU overhead
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
                IsInitialized = true;
                return true;
            }
            else
            {
                _logger.LogWarning("IM19 IMU not detected - no data received within 3 seconds, disposing resources");
                _serialPortManager?.Dispose();
                _serialPortManager = null;
                IsInitialized = false;
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize IM19 IMU on port {PortName}", portName);
            _serialPortManager?.Dispose();
            _serialPortManager = null;
            IsInitialized = false;
            return false;
        }
    }

    private async Task<bool> VerifyImuCommunicationAsync()
    {
        if (_serialPortManager == null || !_serialPortManager.IsConnected)
        {
            _logger.LogWarning("Cannot verify IMU - SerialPortManager is null or not connected");
            return false;
        }

        _logger.LogInformation("Checking for IMU MEMS data within 3 seconds...");

        var dataReceivedEvent = new TaskCompletionSource<bool>();
        int dataEventCount = 0;
        int totalBytesReceived = 0;

        // Subscribe to data events
        EventHandler<byte[]>? verifyHandler = null;
        verifyHandler = (sender, data) =>
        {
            dataEventCount++;
            totalBytesReceived += data.Length;
            _logger.LogInformation("📥 IMU verification: received {ByteCount} bytes (event #{EventCount}, total {Total} bytes)",
                data.Length, dataEventCount, totalBytesReceived);

            // Log first few bytes to help diagnose
            var preview = string.Join(" ", data.Take(Math.Min(8, data.Length)).Select(b => $"{b:X2}"));
            _logger.LogDebug("Data preview: {Preview}", preview);

            if (data.Length > 0)
            {
                dataReceivedEvent.TrySetResult(true);
            }
        };

        _serialPortManager.DataReceived += verifyHandler;

        try
        {
            // Wait for data or timeout (SerialPortManager is already started)
            using var cts = new CancellationTokenSource(InitializationTimeoutMs);
            try
            {
                await dataReceivedEvent.Task.WaitAsync(cts.Token);
                _logger.LogInformation("✅ IM19 IMU detected - received {Total} bytes in {Events} event(s)",
                    totalBytesReceived, dataEventCount);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("❌ IM19 IMU not detected - no data received within {Timeout}ms. Received {Events} events totaling {Bytes} bytes",
                    InitializationTimeoutMs, dataEventCount, totalBytesReceived);
                return false;
            }
        }
        finally
        {
            _serialPortManager.DataReceived -= verifyHandler;
        }
    }

    private async Task ConfigureImuOutputAsync()
    {
        if (_serialPortManager == null || !_serialPortManager.IsConnected)
            return;

        _logger.LogDebug("Configuring IM19 IMU for continuous data output...");

        const string atCommand = "AT+MEMS_OUTPUT=UART1,ON";
        _logger.LogDebug("Sending AT command: {Command}", atCommand);

        var responseReceived = new TaskCompletionSource<string>();
        var responseBuffer = string.Empty;
        var responseBufferLock = new object();

        // Subscribe to DataReceived event to capture AT response
        EventHandler<byte[]>? dataHandler = null;
        dataHandler = (sender, data) =>
        {
            try
            {
                var text = System.Text.Encoding.ASCII.GetString(data);
                lock (responseBufferLock)
                {
                    responseBuffer += text;
                    _logger.LogDebug("Received IMU data during init: {Text}", text.Replace("\r", "\\r").Replace("\n", "\\n"));

                    if (responseBuffer.Contains("OK") || responseBuffer.Contains("ERROR"))
                    {
                        responseReceived.TrySetResult(responseBuffer);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AT response data");
            }
        };

        _serialPortManager.DataReceived += dataHandler;

        try
        {
            _serialPortManager.WriteLine(atCommand);

            using var cts = new CancellationTokenSource(3000);
            try
            {
                var response = await responseReceived.Task.WaitAsync(cts.Token);

                if (response.Contains("OK"))
                {
                    _logger.LogInformation("IM19 IMU MEMS output enabled successfully");
                }
                else if (response.Contains("ERROR"))
                {
                    _logger.LogWarning("IM19 IMU AT command returned ERROR, but continuing - device might already be configured");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("IM19 IMU AT command response timeout - device might already be streaming or not support AT commands");
            }

            _logger.LogDebug("IM19 IMU configuration completed");
        }
        finally
        {
            // Unsubscribe the handler
            _serialPortManager.DataReceived -= dataHandler;
        }
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