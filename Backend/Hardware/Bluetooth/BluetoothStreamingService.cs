using Backend.Configuration;

namespace Backend.Hardware.Bluetooth;

public class BluetoothStreamingService : BackgroundService
{
    private const string BLUETOOTH_PORT = "/dev/rfcomm0";
    private const int WRITE_TIMEOUT_MS = 1000;

    private readonly ILogger<BluetoothStreamingService> _logger;
    private FileStream? _bluetoothStream;
    private bool _isInitializing = false;
    private bool _isReconnecting = false;
    private long _bluetoothBytesSent = 0;
    private long _totalBluetoothBytesSent = 0;
    private DateTime _lastRateUpdate = DateTime.UtcNow;
    private readonly object _connectionLock = new object();

    public BluetoothStreamingService(ILogger<BluetoothStreamingService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Bluetooth Streaming Service started - will connect on first data send");

        // Keep service running and monitor connection
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateDataRatesAsync(stoppingToken);
                await Task.Delay(1000, stoppingToken); // Check every second
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Bluetooth streaming service");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private Task InitializeBluetoothPort()
    {
        try
        {
            _logger.LogDebug("📡 Bluetooth: Opening {Port} for write-only access", BLUETOOTH_PORT);

            // Open FileStream in write-only mode - no reading, no events, just pure output
            _bluetoothStream = new FileStream(
                BLUETOOTH_PORT,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: false
            );

            _logger.LogInformation("📡 Bluetooth: Successfully connected to {Port}", BLUETOOTH_PORT);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 Bluetooth: Failed to open port {Port} - will retry on next data send. Check if device is paired and rfcomm port is bound.", BLUETOOTH_PORT);
            _bluetoothStream = null;
        }

        return Task.CompletedTask;
    }

    public async Task SendData(byte[] data)
    {
        // Check if Bluetooth port exists
        if (!File.Exists(BLUETOOTH_PORT))
        {
            // Port doesn't exist - silently skip (no client connected)
            return;
        }

        // Check if we need to initialize (without holding lock)
        bool shouldInit = false;
        lock (_connectionLock)
        {
            // Don't initialize if reconnecting (device is being removed/cleaned up)
            if (_bluetoothStream == null && !_isInitializing && !_isReconnecting)
            {
                _isInitializing = true;
                shouldInit = true;
            }
        }

        if (shouldInit)
        {
            _logger.LogInformation("📡 Bluetooth: Port {Port} detected! Client connected, attempting to open port...", BLUETOOTH_PORT);

            // Open outside the lock with timeout
            var openTask = Task.Run(() => InitializeBluetoothPort());
            if (await Task.WhenAny(openTask, Task.Delay(3000)) != openTask)
            {
                _logger.LogWarning("📡 Bluetooth: Timeout opening {Port} - device may not be ready yet", BLUETOOTH_PORT);
                lock (_connectionLock)
                {
                    _isInitializing = false;
                }
                return;
            }

            lock (_connectionLock)
            {
                _isInitializing = false;
                if (_bluetoothStream == null)
                {
                    _logger.LogWarning("📡 Bluetooth: Failed to establish connection - port may be busy or not ready");
                    return;
                }
                _logger.LogInformation("✅ Bluetooth: Successfully connected to {Port}, ready to stream data", BLUETOOTH_PORT);
            }
        }
        else if (_isInitializing || _isReconnecting)
        {
            // Another thread is initializing or device is disconnecting, skip this data
            return;
        }

        try
        {
            // Write data to Bluetooth
            _bluetoothStream.Write(data, 0, data.Length);
            _bluetoothStream.Flush();

            _bluetoothBytesSent += data.Length;
            _totalBluetoothBytesSent += data.Length;
        }
        catch (IOException ex)
        {
            // Device disconnected - close stream immediately and trigger single reconnect attempt
            bool shouldReconnect = false;
            lock (_connectionLock)
            {
                if (_bluetoothStream != null && !_isReconnecting)
                {
                    _logger.LogInformation("📡 Bluetooth: Client disconnected, closing stream");
                    try
                    {
                        _bluetoothStream.Close();
                        _bluetoothStream.Dispose();
                    }
                    catch { }
                    _bluetoothStream = null;
                    _isReconnecting = true;
                    shouldReconnect = true;
                }
            }

            if (shouldReconnect)
            {
                await TryReconnectBluetoothPort();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 Bluetooth: Unexpected error sending data");
        }
    }

    private async Task TryReconnectBluetoothPort()
    {
        try
        {
            _logger.LogDebug("📡 Bluetooth: Waiting for device to reconnect...");
            await Task.Delay(1000); // Brief delay to let device disconnect cleanly
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "📡 Bluetooth: Error during reconnect delay");
        }
        finally
        {
            // Reset flags so next connection can be established
            lock (_connectionLock)
            {
                _isInitializing = false;
                _isReconnecting = false;
            }
        }
    }

    private Task UpdateDataRatesAsync(CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;
        var timeDelta = (now - _lastRateUpdate).TotalSeconds;

        // Update rates every 5 seconds
        if (timeDelta >= 5.0)
        {
            // Calculate Bluetooth rate in kbps (kilobits per second)
            var bluetoothRate = (_bluetoothBytesSent * 8.0) / (timeDelta * 1000.0);
            var totalBytesInPeriod = _bluetoothBytesSent;

            // Reset counters
            _bluetoothBytesSent = 0;
            _lastRateUpdate = now;

            if (bluetoothRate > 0)
            {
                //_logger.LogInformation("📊 Bluetooth Rate: {BluetoothRate:F1} kbps ({PeriodBytes} bytes in {Period:F1}s) - Session Total: {SessionTotal} bytes",
                //    bluetoothRate, totalBytesInPeriod, timeDelta, _totalBluetoothBytesSent);
            }
        }

        return Task.CompletedTask;
    }

    public bool IsConnected => _bluetoothStream != null && _bluetoothStream.CanWrite;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Bluetooth Streaming Service");

        // Close FileStream if open
        lock (_connectionLock)
        {
            if (_bluetoothStream != null)
            {
                try
                {
                    _bluetoothStream.Close();
                    _bluetoothStream.Dispose();
                    _bluetoothStream = null;
                    _logger.LogInformation("Bluetooth stream closed");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing Bluetooth stream");
                }
            }
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _logger.LogInformation("Bluetooth Streaming Service disposing");

        // FileStream disposal is handled in StopAsync

        base.Dispose();
    }
}