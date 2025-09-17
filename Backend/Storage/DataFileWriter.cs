using System.Threading.Channels;
using System.Text;
using Backend.Configuration;

namespace Backend.Storage;

public class DataFileWriter : BackgroundService
{
    private readonly ILogger<DataFileWriter> _logger;
    private readonly string _fileName;
    private readonly Channel<string> _dataChannel;
    private readonly ChannelWriter<string> _writer;
    private readonly ChannelReader<string> _reader;

    private readonly List<string> _dataBuffer = new();
    private DateTime _lastFlush = DateTime.UtcNow;
    private string? _currentSessionPath;
    private string? _currentFilePath;
    private bool _driveAvailable = false;

    public DataFileWriter(string fileName, ILogger<DataFileWriter> logger)
    {
        _fileName = fileName;
        _logger = logger;

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
            _logger.LogWarning("Failed to queue data for {FileName} - channel full", _fileName);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataFileWriter started for {FileName}", _fileName);

        var flushTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (!stoppingToken.IsCancellationRequested)
        {
            var flushTask = flushTimer.WaitForNextTickAsync(stoppingToken);
            var dataTask = _reader.WaitToReadAsync(stoppingToken);

            var completedTask = await Task.WhenAny(flushTask.AsTask(), dataTask.AsTask());

            if (completedTask == dataTask.AsTask())
            {
                // Process incoming data
                while (_reader.TryRead(out string? csvLine))
                {
                    _dataBuffer.Add(csvLine);
                }
            }

            // Check flush conditions
            await CheckAndFlushIfNeeded();
        }

        // Final flush on shutdown
        await FlushBuffer();
        _logger.LogInformation("DataFileWriter stopped for {FileName}", _fileName);
    }

    private async Task CheckAndFlushIfNeeded()
    {
        if (_dataBuffer.Count == 0)
            return;

        var timeSinceLastFlush = DateTime.UtcNow - _lastFlush;
        var currentBufferSize = GetBufferSizeInBytes();

        bool shouldFlush = timeSinceLastFlush.TotalSeconds >= SystemConfiguration.LoggingFlushIntervalSeconds ||
                          currentBufferSize >= SystemConfiguration.LoggingMaxBufferSizeBytes;

        if (shouldFlush)
        {
            await FlushBuffer();
        }
    }

    private async Task FlushBuffer()
    {
        if (_dataBuffer.Count == 0)
            return;

        try
        {
            EnsureSessionPathExists();

            if (!_driveAvailable || string.IsNullOrEmpty(_currentFilePath))
            {
                _logger.LogWarning("No USB drive available - dropping {Count} data lines for {FileName}",
                    _dataBuffer.Count, _fileName);
                _dataBuffer.Clear();
                return;
            }

            var csvContent = new StringBuilder();

            // Add header if file doesn't exist
            if (!File.Exists(_currentFilePath))
            {
                csvContent.AppendLine(GetCsvHeader());
            }

            // Add all buffered data
            foreach (var line in _dataBuffer)
            {
                csvContent.AppendLine(line);
            }

            await File.AppendAllTextAsync(_currentFilePath, csvContent.ToString());

            _logger.LogDebug("Flushed {Count} lines to {FilePath}", _dataBuffer.Count, _currentFilePath);

            _dataBuffer.Clear();
            _lastFlush = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing data buffer for {FileName}", _fileName);
        }
    }

    private void EnsureSessionPathExists()
    {
        try
        {
            // Check for USB drive
            var mediaDir = "/media";
            if (!Directory.Exists(mediaDir))
            {
                _driveAvailable = false;
                return;
            }

            var drives = Directory.GetDirectories(mediaDir);
            if (drives.Length == 0)
            {
                _driveAvailable = false;
                return;
            }

            var driveRoot = drives[0]; // Assume first drive
            var loggingDir = Path.Combine(driveRoot, "Logging");

            // Create session directory if needed
            var sessionFolder = DateTime.Now.ToString("yyyy-MM-dd-HH-mm");
            var sessionPath = Path.Combine(loggingDir, sessionFolder);

            if (_currentSessionPath != sessionPath)
            {
                Directory.CreateDirectory(sessionPath);
                _currentSessionPath = sessionPath;
                _currentFilePath = Path.Combine(sessionPath, _fileName);

                _logger.LogInformation("Created new logging session: {SessionPath}", sessionPath);
            }

            _driveAvailable = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring session path exists");
            _driveAvailable = false;
        }

    }

    private int GetBufferSizeInBytes()
    {
        return _dataBuffer.Sum(line => Encoding.UTF8.GetByteCount(line + Environment.NewLine));
    }

    private string GetCsvHeader()
    {
        return _fileName.ToLower() switch
        {
            "imu.txt" => "timestamp,accel_x,accel_y,accel_z,gyro_x,gyro_y,gyro_z,mag_x,mag_y,mag_z",
            "gnss.raw" => "timestamp,data", // Can be customized later
            "system.txt" => "timestamp,cpu_percent,memory_mb,temperature_c", // Can be customized later
            _ => "timestamp,data"
        };
    }

    public override void Dispose()
    {
        _writer.Complete();
        base.Dispose();
    }
}