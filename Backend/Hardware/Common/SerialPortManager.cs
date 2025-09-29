using System.IO.Ports;

namespace Backend.Hardware.Common;

/// <summary>
/// Manages serial port communication with event-driven approach and watchdog-triggered polling fallback.
/// Provides reliable data collection with configurable buffer management and rate tracking.
/// Uses zero-CPU polling when events work, switches to aggressive polling when events fail.
/// </summary>
public class SerialPortManager : IDisposable
{
    private readonly ILogger<SerialPortManager> _logger;
    private readonly string _deviceName;
    private readonly string _portName;
    private readonly int _baudRate;
    private readonly Parity _parity;
    private readonly int _dataBits;
    private readonly StopBits _stopBits;
    private readonly int _pollingIntervalMs;
    private readonly int _readBufferSize;
    private readonly int _maxBufferSize;
    private readonly int _rateUpdateIntervalMs;
    private SerialPort? _serialPort;
    private readonly List<byte> _dataBuffer = new();
    private readonly object _dataBufferLock = new();
    private readonly object _pollingLock = new();
    private Timer? _eventWatchdogTimer;
    private volatile bool _eventsAreHealthy = true;
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

    public SerialPortManager(
        string deviceName,
        string portName,
        int baudRate,
        ILogger<SerialPortManager> logger,
        int pollingIntervalMs = 100,
        int readBufferSize = 4096,
        int maxBufferSize = 1048576,
        int rateUpdateIntervalMs = 1000,
        Parity parity = Parity.None,
        int dataBits = 8,
        StopBits stopBits = StopBits.One)
    {
        _deviceName = deviceName;
        _portName = portName;
        _baudRate = baudRate;
        _parity = parity;
        _dataBits = dataBits;
        _stopBits = stopBits;
        _pollingIntervalMs = pollingIntervalMs;
        _readBufferSize = readBufferSize;
        _maxBufferSize = maxBufferSize;
        _rateUpdateIntervalMs = rateUpdateIntervalMs;
        _logger = logger;
    }

    /// <summary>
    /// Starts the serial port manager by creating a new serial port from config
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SerialPortManager));

        if (string.IsNullOrEmpty(_portName))
            throw new InvalidOperationException("PortName must be specified");

        var serialPort = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
        {
            ReadTimeout = 1000,
            WriteTimeout = 1000,
            RtsEnable = true,
            DtrEnable = true
        };

        try
        {
            serialPort.Open();
            await StartAsync(serialPort, cancellationToken);
        }
        catch
        {
            serialPort?.Dispose();
            throw;
        }
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
            _logger.LogWarning("Serial port not available for {DeviceName}", _deviceName);
            return;
        }

        _logger.LogInformation("SerialPortManager started for {DeviceName} on {PortName} at {BaudRate} baud",
            _deviceName, _serialPort.PortName, _serialPort.BaudRate);

        // Subscribe to DataReceived event
        _serialPort.DataReceived += OnDataReceived;

        // Start rate monitoring and event watchdog
        _ = Task.Run(() => RateUpdateLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        ResetEventWatchdog();

        _logger.LogInformation("Event-driven watchdog setup completed for {DeviceName}", _deviceName);
    }

    /// <summary>
    /// Enables or disables watchdog-triggered polling. Events will still work when polling is disabled.
    /// When disabled, the watchdog timer still runs but won't trigger aggressive polling on timeout.
    /// </summary>
    public void SetPollingEnabled(bool enabled)
    {
        lock (_pollingLock)
        {
            if (_isPollingEnabled != enabled)
            {
                _isPollingEnabled = enabled;
                _logger.LogInformation("Polling {Status} for {DeviceName}",
                    enabled ? "enabled" : "disabled", _deviceName);
            }
        }
    }

    private async void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            await ReadAndProcessDataAsync(fromPolling: false);

            // Reset the watchdog - events are working
            ResetEventWatchdog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DataReceived event handler for {DeviceName}", _deviceName);
        }
    }

    private void ResetEventWatchdog()
    {
        if (_disposed) return;

        _eventsAreHealthy = true;

        // Cancel any existing timer and start a new one
        _eventWatchdogTimer?.Dispose();
        _eventWatchdogTimer = new Timer(OnEventWatchdogTimeout, null, _pollingIntervalMs, Timeout.Infinite);
    }

    private async void OnEventWatchdogTimeout(object? state)
    {
        try
        {
            // Timer expired - events might have failed
            if (_serialPort?.IsOpen == true && _serialPort.BytesToRead > 0)
            {
                bool pollingEnabled;
                lock (_pollingLock)
                {
                    pollingEnabled = _isPollingEnabled;
                }

                if (pollingEnabled)
                {
                    _logger.LogInformation("🔍 EVENT WATCHDOG ({DeviceName}): Events failed, switching to polling mode. Found {BytesToRead} bytes",
                        _deviceName, _serialPort.BytesToRead);
                    _eventsAreHealthy = false;

                    // Start aggressive polling until events resume
                    _ = Task.Run(() => AggressivePollingLoop(_cancellationTokenSource?.Token ?? CancellationToken.None));
                }
            }
            else if (_serialPort?.IsOpen == true)
            {
                // No data available, just reset watchdog for next check
                ResetEventWatchdog();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in event watchdog timeout for {DeviceName}", _deviceName);
        }
    }

    private async Task AggressivePollingLoop(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔄 Starting aggressive polling for {DeviceName}", _deviceName);

        while (!_eventsAreHealthy && _serialPort?.IsOpen == true && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_serialPort.BytesToRead > 0)
                {
                    await ReadAndProcessDataAsync(fromPolling: true);

                    // Check if events have resumed by setting a watchdog
                    ResetEventWatchdog();
                    await Task.Delay(_pollingIntervalMs, stoppingToken); // Give events a chance to resume
                }
                else
                {
                    await Task.Delay(_pollingIntervalMs, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in aggressive polling loop for {DeviceName}", _deviceName);
                await Task.Delay(_pollingIntervalMs, stoppingToken);
            }
        }

        if (_eventsAreHealthy)
        {
            _logger.LogInformation("✅ Events resumed for {DeviceName}, stopping aggressive polling", _deviceName);
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
                var bytesToRead = Math.Min(_serialPort.BytesToRead, _readBufferSize);
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
                        _deviceName, totalBytesRead);
                }

                await ProcessBufferedDataAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading data for {DeviceName}", _deviceName);
        }
    }

    private async Task ProcessBufferedDataAsync()
    {
        const int maxProcessingCycles = 50;
        int processed = 0;

        // Trim buffer if it exceeds maximum size
        lock (_dataBufferLock)
        {
            if (_dataBuffer.Count > _maxBufferSize)
            {
                int toDrop = _dataBuffer.Count - _maxBufferSize;
                _logger.LogWarning("Buffer exceeded {MaxSize} bytes for {DeviceName}; dropping {Drop} oldest bytes",
                    _maxBufferSize, _deviceName, toDrop);
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
                _logger.LogError(ex, "Error in data processing callback for {DeviceName}", _deviceName);
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
                await Task.Delay(_rateUpdateIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in rate update loop for {DeviceName}", _deviceName);
                await Task.Delay(_rateUpdateIntervalMs, stoppingToken);
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

            _logger.LogDebug("Rate updated for {DeviceName}: {Rate:F1} kbps", _deviceName, _currentReceiveRate);
        }
    }

    public async Task StopAsync()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Stopping SerialPortManager for {DeviceName}", _deviceName);

        if (_serialPort != null)
        {
            _serialPort.DataReceived -= OnDataReceived;
        }

        // Stop the event watchdog timer
        _eventWatchdogTimer?.Dispose();
        _eventWatchdogTimer = null;
        _eventsAreHealthy = false;

        _cancellationTokenSource?.Cancel();

        // Give background tasks time to complete
        await Task.Delay(200);

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    /// <summary>
    /// Writes data to the serial port
    /// </summary>
    public void Write(string data)
    {
        if (_serialPort?.IsOpen == true)
        {
            _serialPort.Write(data);
        }
        else
        {
            throw new InvalidOperationException($"Serial port not open for {_deviceName}");
        }
    }

    /// <summary>
    /// Writes binary data to the serial port
    /// </summary>
    public void Write(byte[] data, int offset, int count)
    {
        if (_serialPort?.IsOpen == true)
        {
            _serialPort.Write(data, offset, count);
        }
        else
        {
            throw new InvalidOperationException($"Serial port not open for {_deviceName}");
        }
    }

    /// <summary>
    /// Writes a line to the serial port
    /// </summary>
    public void WriteLine(string data)
    {
        if (_serialPort?.IsOpen == true)
        {
            _serialPort.WriteLine(data);
        }
        else
        {
            throw new InvalidOperationException($"Serial port not open for {_deviceName}");
        }
    }

    /// <summary>
    /// Reads existing data from the serial port buffer
    /// </summary>
    public string ReadExisting()
    {
        if (_serialPort?.IsOpen == true)
        {
            return _serialPort.ReadExisting();
        }
        else
        {
            throw new InvalidOperationException($"Serial port not open for {_deviceName}");
        }
    }

    /// <summary>
    /// Discards the input buffer
    /// </summary>
    public void DiscardInBuffer()
    {
        if (_serialPort?.IsOpen == true)
        {
            _serialPort.DiscardInBuffer();
        }
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

        // Dispose the event watchdog timer
        _eventWatchdogTimer?.Dispose();
        _eventWatchdogTimer = null;

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}

