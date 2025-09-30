using System.IO.Ports;
using System.Threading.Channels;

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
    private readonly int _minBatchSize;
    private readonly int _batchTimeoutMs;
    private readonly bool _forcePollingMode;
    private SerialPort? _serialPort;
    private readonly List<byte> _dataBuffer = new();
    private readonly object _dataBufferLock = new();
    private readonly object _pollingLock = new();
    private readonly object _watchdogLock = new();
    private Timer? _eventWatchdogTimer;
    private bool _eventsAreHealthy = true;
    private bool _aggressivePollingRunning = false;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed = false;
    private bool _isPollingEnabled = true;

    // Producer-Consumer infrastructure
    private readonly Channel<byte[]> _dataChannel;
    private readonly ChannelWriter<byte[]> _dataWriter;
    private readonly ChannelReader<byte[]> _dataReader;
    private Task? _processingTask;

    // Rate tracking
    private long _bytesReceived = 0;
    private DateTime _lastRateUpdate = DateTime.UtcNow;
    private double _currentReceiveRate = 0.0;

    // DIAGNOSTIC TRACKING (disabled for performance)

    // Source-level batching (in OnDataReceived before channel write) - using byte array for performance
    private byte[] _sourceBuffer = new byte[4096];
    private int _sourceBufferLength = 0;
    private readonly object _sourceBufferLock = new();
    private DateTime _lastSourceBatchTime = DateTime.UtcNow;

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
        StopBits stopBits = StopBits.One,
        int minBatchSize = 1,
        int batchTimeoutMs = 20,
        bool forcePollingMode = false)
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
        _minBatchSize = minBatchSize;
        _batchTimeoutMs = batchTimeoutMs;
        _forcePollingMode = forcePollingMode;
        _logger = logger;

        // Initialize producer-consumer channel
        _dataChannel = Channel.CreateUnbounded<byte[]>();
        _dataWriter = _dataChannel.Writer;
        _dataReader = _dataChannel.Reader;
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

        if (_forcePollingMode)
        {
            // Polling-only mode - don't subscribe to events, just start aggressive polling
            _logger.LogInformation("Force polling mode enabled for {DeviceName} - using polling at {IntervalMs}ms intervals instead of events",
                _deviceName, _pollingIntervalMs);

            _eventsAreHealthy = false; // Disable event watchdog
            _processingTask = Task.Run(() => ProcessingLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            _ = Task.Run(() => RateUpdateLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            _ = Task.Run(() => AggressivePollingLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }
        else
        {
            // Event-driven mode with fallback to polling
            _serialPort.DataReceived += OnDataReceived;
            _processingTask = Task.Run(() => ProcessingLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            _ = Task.Run(() => RateUpdateLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            ResetEventWatchdog();
            _logger.LogInformation("Producer-Consumer pattern and event-driven watchdog setup completed for {DeviceName}", _deviceName);
        }
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

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            // Lightweight producer - read data and batch before queuing
            if (_serialPort?.IsOpen == true && _serialPort.BytesToRead > 0)
            {
                var bytesToRead = Math.Min(_serialPort.BytesToRead, _readBufferSize);
                var buffer = new byte[bytesToRead];
                var bytesRead = _serialPort.Read(buffer, 0, bytesToRead);

                if (bytesRead > 0)
                {
                    _bytesReceived += bytesRead;

                    // Add to source buffer for batching (using byte array for performance)
                    bool shouldFlush = false;
                    byte[]? dataToWrite = null;

                    lock (_sourceBufferLock)
                    {
                        // Ensure buffer has space
                        if (_sourceBufferLength + bytesRead > _sourceBuffer.Length)
                        {
                            // Resize if needed
                            var newBuffer = new byte[Math.Max(_sourceBuffer.Length * 2, _sourceBufferLength + bytesRead)];
                            Array.Copy(_sourceBuffer, 0, newBuffer, 0, _sourceBufferLength);
                            _sourceBuffer = newBuffer;
                        }

                        // Copy new data to buffer
                        Array.Copy(buffer, 0, _sourceBuffer, _sourceBufferLength, bytesRead);
                        _sourceBufferLength += bytesRead;

                        // Check if we should flush
                        var timeSinceLastBatch = (DateTime.UtcNow - _lastSourceBatchTime).TotalMilliseconds;
                        if (_sourceBufferLength >= _minBatchSize || timeSinceLastBatch >= _batchTimeoutMs)
                        {
                            shouldFlush = true;
                            dataToWrite = new byte[_sourceBufferLength];
                            Array.Copy(_sourceBuffer, 0, dataToWrite, 0, _sourceBufferLength);
                            _sourceBufferLength = 0;
                            _lastSourceBatchTime = DateTime.UtcNow;
                        }
                    }

                    // Write to channel if we have a batch ready
                    if (shouldFlush && dataToWrite != null)
                    {
                        if (_dataWriter.TryWrite(dataToWrite))
                        {
                            // Reset the watchdog - events are working
                            ResetEventWatchdog();
                        }
                        else
                        {
                            _logger.LogWarning("Failed to queue {BytesRead} bytes - channel may be closed for {DeviceName}",
                                dataToWrite.Length, _deviceName);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DataReceived event handler for {DeviceName}", _deviceName);
        }
    }

    private void ResetEventWatchdog()
    {
        if (_disposed) return;

        lock (_watchdogLock)
        {
            _eventsAreHealthy = true;

            // Cancel any existing timer and start a new one
            _eventWatchdogTimer?.Dispose();
            _eventWatchdogTimer = new Timer(OnEventWatchdogTimeout, null, _pollingIntervalMs, Timeout.Infinite);
        }
    }

    private async void OnEventWatchdogTimeout(object? state)
    {
        try
        {
            // Timer expired - events might have failed
            if (_serialPort?.IsOpen == true && _serialPort.BytesToRead > 0)
            {
                bool pollingEnabled;
                bool shouldStartPolling = false;

                lock (_pollingLock)
                {
                    pollingEnabled = _isPollingEnabled;
                }

                if (pollingEnabled)
                {
                    lock (_watchdogLock)
                    {
                        if (!_aggressivePollingRunning)
                        {
                            _logger.LogInformation("🔍 EVENT WATCHDOG ({DeviceName}): Events failed, switching to polling mode. Found {BytesToRead} bytes",
                                _deviceName, _serialPort.BytesToRead);
                            _eventsAreHealthy = false;
                            _aggressivePollingRunning = true;
                            shouldStartPolling = true;
                        }
                    }

                    if (shouldStartPolling)
                    {
                        // Start aggressive polling until events resume
                        _ = Task.Run(() => AggressivePollingLoop(_cancellationTokenSource?.Token ?? CancellationToken.None));
                    }
                }
            }

            // Always restart watchdog for next check if port is still open
            if (_serialPort?.IsOpen == true)
            {
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
        int consecutiveEmptyReads = 0;
        const int emptyReadsBeforeEventTest = 3; // Test events after 3 empty cycles

        try
        {
            bool shouldContinue = true;
            lock (_watchdogLock)
            {
                shouldContinue = !_eventsAreHealthy;
            }

            while (shouldContinue && _serialPort?.IsOpen == true && !stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_serialPort.BytesToRead > 0)
                    {
                        // Data available - read and add to batch buffer (same logic as OnDataReceived)
                        var bytesToRead = Math.Min(_serialPort.BytesToRead, _readBufferSize);
                        var buffer = new byte[bytesToRead];
                        var bytesRead = _serialPort.Read(buffer, 0, bytesToRead);

                        if (bytesRead > 0)
                        {
                            _bytesReceived += bytesRead;
                            consecutiveEmptyReads = 0; // Reset counter since we found data

                            // Simplified: just write directly to channel since we're polling at controlled intervals
                            // No need for complex batching logic - the polling interval controls the batch rate
                            var data = bytesRead == buffer.Length ? buffer : buffer[..bytesRead];
                            if (!_dataWriter.TryWrite(data))
                            {
                                _logger.LogWarning("Failed to queue {BytesRead} bytes during polling - channel may be closed for {DeviceName}",
                                    bytesRead, _deviceName);
                            }
                        }

                        // Wait polling interval before next read
                        await Task.Delay(_pollingIntervalMs, stoppingToken);
                    }
                    else
                    {
                        consecutiveEmptyReads++;

                        if (consecutiveEmptyReads >= emptyReadsBeforeEventTest)
                        {
                            // No data for several cycles - test if events have resumed
                            _logger.LogDebug("🔄 Testing event recovery for {DeviceName} after {EmptyReads} empty cycles",
                                _deviceName, consecutiveEmptyReads);

                            ResetEventWatchdog(); // This sets _eventsAreHealthy = true and starts watchdog

                            // Give events a chance to prove they're working
                            await Task.Delay(_pollingIntervalMs, stoppingToken);

                            // If watchdog timer expires again, _eventsAreHealthy will be set to false
                            // and we'll continue this loop. If events work, this loop will exit.
                            consecutiveEmptyReads = 0;
                        }
                        else
                        {
                            // Continue aggressive polling - don't wait full interval for first few empty reads
                            await Task.Delay(_pollingIntervalMs / 2, stoppingToken);
                        }
                    }

                    // Check if we should continue (thread-safe)
                    lock (_watchdogLock)
                    {
                        shouldContinue = !_eventsAreHealthy;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in aggressive polling loop for {DeviceName}", _deviceName);
                    await Task.Delay(_pollingIntervalMs, stoppingToken);
                    consecutiveEmptyReads = 0; // Reset on error
                }
            }
        }
        finally
        {
            lock (_watchdogLock)
            {
                _aggressivePollingRunning = false;

                if (_eventsAreHealthy)
                {
                    _logger.LogInformation("✅ Events resumed for {DeviceName}, stopping aggressive polling", _deviceName);
                }
            }
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

    private async Task ProcessingLoop(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔄 Producer-Consumer processing loop started for {DeviceName}", _deviceName);

        try
        {
            await foreach (var data in _dataReader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // Add data to buffer and process immediately
                    // Batching is now controlled at source level (OnDataReceived or polling interval)
                    lock (_dataBufferLock)
                    {
                        _dataBuffer.AddRange(data);
                    }

                    await ProcessBufferedDataAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing data batch in consumer for {DeviceName}", _deviceName);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
            _logger.LogInformation("Processing loop cancelled for {DeviceName}", _deviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in processing loop for {DeviceName}", _deviceName);
        }

        _logger.LogInformation("🔄 Producer-Consumer processing loop stopped for {DeviceName}", _deviceName);
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

        // Unsubscribe from events first to prevent new data from arriving
        if (_serialPort != null)
        {
            _serialPort.DataReceived -= OnDataReceived;
        }

        // Stop the event watchdog timer
        lock (_watchdogLock)
        {
            _eventWatchdogTimer?.Dispose();
            _eventWatchdogTimer = null;
            _eventsAreHealthy = false;
        }

        // Close the channel to signal processing loop to stop
        _dataWriter.Complete();

        _cancellationTokenSource?.Cancel();

        // Wait for processing task to complete
        if (_processingTask != null)
        {
            try
            {
                await _processingTask.WaitAsync(TimeSpan.FromSeconds(5));
                _logger.LogInformation("Processing task completed for {DeviceName}", _deviceName);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Processing task did not complete within timeout for {DeviceName}", _deviceName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for processing task to complete for {DeviceName}", _deviceName);
            }
            _processingTask = null;
        }

        // Give remaining background tasks time to complete
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

        // Unsubscribe from events first to prevent new data from arriving
        if (_serialPort != null)
        {
            _serialPort.DataReceived -= OnDataReceived;
        }

        // Dispose the event watchdog timer
        lock (_watchdogLock)
        {
            _eventWatchdogTimer?.Dispose();
            _eventWatchdogTimer = null;
            _eventsAreHealthy = false;
        }

        // Close the channel
        _dataWriter.Complete();

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}

