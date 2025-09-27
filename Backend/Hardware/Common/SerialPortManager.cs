using System.IO.Ports;

namespace Backend.Hardware.Common;

/// <summary>
/// Manages serial port communication with event-driven approach and backup polling fallback.
/// Provides reliable data collection with configurable buffer management and rate tracking.
/// </summary>
public class SerialPortManager : IDisposable
{
    private readonly ILogger<SerialPortManager> _logger;
    private readonly SerialPortConfig _config;
    private SerialPort? _serialPort;
    private readonly List<byte> _dataBuffer = new();
    private readonly object _dataBufferLock = new();
    private readonly object _timeLock = new();
    private readonly object _pollingLock = new();
    private DateTime _lastEventTime = DateTime.UtcNow;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed = false;
    private bool _isPollingEnabled = true;

    // Rate tracking
    private long _bytesReceived = 0;
    private DateTime _lastRateUpdate = DateTime.UtcNow;
    private double _currentReceiveRate = 0.0;

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<double>? RateUpdated;

    public bool IsConnected => _serialPort?.IsOpen == true;
    public double CurrentReceiveRate => _currentReceiveRate;
    public bool IsPollingEnabled
    {
        get
        {
            lock (_pollingLock)
            {
                return _isPollingEnabled;
            }
        }
    }

    public SerialPortManager(SerialPortConfig config, ILogger<SerialPortManager> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Starts the serial port manager with the provided serial port
    /// </summary>
    public async Task StartAsync(SerialPort serialPort, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SerialPortManager));

        _serialPort = serialPort;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (_serialPort == null || !_serialPort.IsOpen)
        {
            _logger.LogWarning("Serial port not available for {DeviceName}", _config.DeviceName);
            return;
        }

        _logger.LogInformation("SerialPortManager started for {DeviceName} on {PortName} at {BaudRate} baud",
            _config.DeviceName, _serialPort.PortName, _serialPort.BaudRate);

        // Subscribe to DataReceived event
        _serialPort.DataReceived += OnDataReceived;

        // Start backup polling and rate monitoring
        _ = Task.Run(() => BackupPollingLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        _ = Task.Run(() => RateUpdateLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

        _logger.LogInformation("Event + backup polling setup completed for {DeviceName}", _config.DeviceName);
    }

    /// <summary>
    /// Enables or disables backup polling. Events will still work when polling is disabled.
    /// </summary>
    public void SetPollingEnabled(bool enabled)
    {
        lock (_pollingLock)
        {
            if (_isPollingEnabled != enabled)
            {
                _isPollingEnabled = enabled;
                _logger.LogInformation("Polling {Status} for {DeviceName}",
                    enabled ? "enabled" : "disabled", _config.DeviceName);
            }
        }
    }

    private async void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            lock (_timeLock)
            {
                _lastEventTime = DateTime.UtcNow;
            }

            await ReadAndProcessDataAsync(fromPolling: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DataReceived event handler for {DeviceName}", _config.DeviceName);
        }
    }

    private async Task BackupPollingLoop(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backup polling started for {DeviceName} (triggers if events fail)", _config.DeviceName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    // Check if polling is enabled
                    bool pollingEnabled;
                    lock (_pollingLock)
                    {
                        pollingEnabled = _isPollingEnabled;
                    }

                    if (pollingEnabled)
                    {
                        var now = DateTime.UtcNow;
                        DateTime lastEvent;

                        lock (_timeLock)
                        {
                            lastEvent = _lastEventTime;
                        }

                        var timeSinceLastEvent = now - lastEvent;

                        // Simple logic: if data available and no event within configured interval, poll immediately
                        var bytesToRead = _serialPort.BytesToRead;
                        if (bytesToRead > 0 && timeSinceLastEvent.TotalMilliseconds >= _config.BackupPollingIntervalMs)
                        {
                            _logger.LogInformation("🔍 BACKUP POLL ({DeviceName}): Events stopped working! Found {BytesToRead} bytes after {TimeSinceEvent:F1}ms without events",
                                _config.DeviceName, bytesToRead, timeSinceLastEvent.TotalMilliseconds);

                            await ReadAndProcessDataAsync(fromPolling: true);
                        }
                    }
                }

                await Task.Delay(_config.CheckIntervalMs, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in backup polling loop for {DeviceName}", _config.DeviceName);
                await Task.Delay(_config.CheckIntervalMs, stoppingToken);
            }
        }
    }

    private async Task ReadAndProcessDataAsync(bool fromPolling = false)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return;

        try
        {
            // Smart burst reading - keep reading while data is available
            bool dataRead = false;
            int totalBytesRead = 0;

            while (_serialPort.BytesToRead > 0)
            {
                var bytesToRead = Math.Min(_serialPort.BytesToRead, _config.ReadBufferSize);
                var buffer = new byte[bytesToRead];
                var bytesRead = _serialPort.Read(buffer, 0, bytesToRead);

                if (bytesRead == 0)
                    break;

                dataRead = true;
                totalBytesRead += bytesRead;
                _bytesReceived += bytesRead;

                lock (_dataBufferLock)
                {
                    for (int i = 0; i < bytesRead; i++)
                    {
                        _dataBuffer.Add(buffer[i]);
                    }
                }

                if (bytesRead < bytesToRead)
                    break;
            }

            if (dataRead)
            {
                if (fromPolling)
                {
                    _logger.LogInformation("📥 BACKUP POLL ({DeviceName}): Read {BytesRead} bytes in burst mode",
                        _config.DeviceName, totalBytesRead);
                }

                await ProcessBufferedDataAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading data for {DeviceName}", _config.DeviceName);
        }
    }

    private async Task ProcessBufferedDataAsync()
    {
        const int maxProcessingCycles = 50;
        int processed = 0;

        // Trim buffer if it exceeds maximum size
        lock (_dataBufferLock)
        {
            if (_dataBuffer.Count > _config.MaxBufferSize)
            {
                int toDrop = _dataBuffer.Count - _config.MaxBufferSize;
                _logger.LogWarning("Buffer exceeded {MaxSize} bytes for {DeviceName}; dropping {Drop} oldest bytes",
                    _config.MaxBufferSize, _config.DeviceName, toDrop);
                _dataBuffer.RemoveRange(0, toDrop);
            }

            if (_dataBuffer.Count == 0)
                return;
        }

        while (processed < maxProcessingCycles)
        {
            byte[] dataToProcess;

            lock (_dataBufferLock)
            {
                if (_dataBuffer.Count == 0)
                    break;

                // Extract all available data
                dataToProcess = _dataBuffer.ToArray();
                _dataBuffer.Clear();
            }

            try
            {
                // Notify subscribers with the raw data
                DataReceived?.Invoke(this, dataToProcess);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in data processing callback for {DeviceName}", _config.DeviceName);
            }

            processed++;
        }
    }

    private async Task RateUpdateLoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateRateAsync();
                await Task.Delay(_config.RateUpdateIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in rate update loop for {DeviceName}", _config.DeviceName);
                await Task.Delay(_config.RateUpdateIntervalMs, stoppingToken);
            }
        }
    }

    private async Task UpdateRateAsync()
    {
        var now = DateTime.UtcNow;
        var timeDelta = (now - _lastRateUpdate).TotalSeconds;

        if (timeDelta >= 1.0)
        {
            _currentReceiveRate = (_bytesReceived * 8.0) / (timeDelta * 1000.0);
            _bytesReceived = 0;
            _lastRateUpdate = now;

            RateUpdated?.Invoke(this, _currentReceiveRate);

            _logger.LogDebug("Rate updated for {DeviceName}: {Rate:F1} kbps", _config.DeviceName, _currentReceiveRate);
        }
    }

    public async Task StopAsync()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Stopping SerialPortManager for {DeviceName}", _config.DeviceName);

        if (_serialPort != null)
        {
            _serialPort.DataReceived -= OnDataReceived;
        }

        _cancellationTokenSource?.Cancel();

        // Give background tasks time to complete
        await Task.Delay(200);

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_serialPort != null)
        {
            _serialPort.DataReceived -= OnDataReceived;
        }

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}

/// <summary>
/// Configuration for SerialPortManager
/// </summary>
public class SerialPortConfig
{
    public string DeviceName { get; set; } = "Unknown";
    public int BackupPollingIntervalMs { get; set; } = 100;
    public int CheckIntervalMs { get; set; } = 100;
    public int ReadBufferSize { get; set; } = 4096;
    public int MaxBufferSize { get; set; } = 1048576; // 1MB
    public int RateUpdateIntervalMs { get; set; } = 1000;
}