using System.Threading.Channels;
using System.Text;

namespace Backend.Storage;

public class LocalFileWriter : BackgroundService
{
    private const string LocalLogDirectory = "/var/log/subterra/telemetry";
    private const string FilePattern = "*.txt";

    private readonly ILogger<LocalFileWriter> _logger;
    private readonly string _basePath;
    private readonly Channel<string> _dataChannel;
    private readonly ChannelWriter<string> _writer;
    private readonly ChannelReader<string> _reader;

    private readonly List<string> _dataBuffer = new();
    private DateTime _lastFlush = DateTime.UtcNow;
    private string? _currentFilePath;
    private int _sessionNumber;

    public LocalFileWriter(ILogger<LocalFileWriter> logger)
    {
        _logger = logger;
        _basePath = LocalLogDirectory;

        var options = new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };

        _dataChannel = Channel.CreateBounded<string>(options);
        _writer = _dataChannel.Writer;
        _reader = _dataChannel.Reader;
    }

    public void WriteData(string csvLine)
    {
        if (!_writer.TryWrite(csvLine))
        {
            _logger.LogWarning("Failed to queue data for local battery log - channel full");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LocalFileWriter started for battery logging");

        // Initialize local directory and determine session number
        InitializeLocalStorage();

        var lastFlushTime = DateTime.UtcNow;
        const int flushIntervalMs = 1000; // 1 second

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Calculate timeout for next flush
                    var timeSinceLastFlush = DateTime.UtcNow - lastFlushTime;
                    var timeUntilNextFlush = TimeSpan.FromMilliseconds(flushIntervalMs) - timeSinceLastFlush;

                    if (timeUntilNextFlush <= TimeSpan.Zero)
                    {
                        await FlushBuffer();
                        lastFlushTime = DateTime.UtcNow;
                        timeUntilNextFlush = TimeSpan.FromMilliseconds(flushIntervalMs);
                    }

                    // Wait for data or timeout
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeoutCts.CancelAfter(timeUntilNextFlush);

                    try
                    {
                        if (await _reader.WaitToReadAsync(timeoutCts.Token))
                        {
                            // Process incoming data
                            while (_reader.TryRead(out string? data))
                            {
                                _dataBuffer.Add(data);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                    {
                        // Timeout occurred, continue to flush
                    }
                }
                catch (OperationCanceledException)
                {
                    // Main cancellation token was cancelled - exit the loop
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in LocalFileWriter main loop");
                    await Task.Delay(1000, stoppingToken); // Brief delay before retry
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }

        // Final flush on shutdown
        await FlushBuffer();
        _logger.LogInformation("LocalFileWriter stopped");
    }

    private void InitializeLocalStorage()
    {
        try
        {
            // Ensure directory exists
            if (!Directory.Exists(_basePath))
            {
                Directory.CreateDirectory(_basePath);
                _logger.LogInformation("Created local logging directory: {Path}", _basePath);

                // Set permissions to 755 (rwxr-xr-x) - owner can write, all can read
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    try
                    {
                        File.SetUnixFileMode(_basePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                        _logger.LogInformation("Set directory permissions to 755 (world-readable)");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not set permissions on directory - files may not be world-readable");
                    }
                }
            }

            // Find highest existing session number
            _sessionNumber = GetNextSessionNumber();
            _currentFilePath = Path.Combine(_basePath, $"{_sessionNumber}.txt");

            _logger.LogInformation("Local battery data will be written to: {FilePath}", _currentFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing local storage - logging will be disabled");
            _currentFilePath = null;
        }
    }

    private int GetNextSessionNumber()
    {
        try
        {
            var existingFiles = Directory.GetFiles(_basePath, FilePattern);

            if (existingFiles.Length == 0)
            {
                _logger.LogInformation("No existing battery log files found, starting with session 1");
                return 1;
            }

            int maxNumber = 0;
            foreach (var file in existingFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var parts = fileName.Split('-');

                if (parts.Length > 0 && int.TryParse(parts[0], out int number))
                {
                    if (number > maxNumber)
                        maxNumber = number;
                }
            }

            int nextNumber = maxNumber + 1;
            _logger.LogInformation("Found {Count} existing battery log files, highest number: {Max}, using: {Next}",
                existingFiles.Length, maxNumber, nextNumber);

            return nextNumber;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading existing session files, starting from 1");
            return 1;
        }
    }

    private async Task FlushBuffer()
    {
        if (_dataBuffer.Count == 0)
            return;

        if (string.IsNullOrEmpty(_currentFilePath))
        {
            _logger.LogWarning("No valid file path - cannot write {Count} buffered items", _dataBuffer.Count);
            return;
        }

        try
        {
            var textContent = new StringBuilder();
            foreach (var line in _dataBuffer)
            {
                textContent.AppendLine(line);
            }

            await File.AppendAllTextAsync(_currentFilePath, textContent.ToString());

            _logger.LogDebug("Flushed {Count} items to local battery log", _dataBuffer.Count);

            _dataBuffer.Clear();
            _lastFlush = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing local battery data buffer");
        }
    }

    public override void Dispose()
    {
        try
        {
            if (!_writer.TryComplete())
            {
                _logger.LogDebug("Channel writer was already completed");
            }
        }
        catch (InvalidOperationException)
        {
            _logger.LogDebug("Channel writer was already closed");
        }

        base.Dispose();
    }
}
