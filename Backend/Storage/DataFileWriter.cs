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
    private string? _currentDrivePath;

    // Public properties to expose drive information
    public string? CurrentDrivePath => _currentDrivePath;
    public string? CurrentFilePath => _currentFilePath;
    public string? CurrentSessionPath => _currentSessionPath;
    public bool IsDriveAvailable => _driveAvailable;

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
                        // Time to flush, check conditions
                        await CheckAndFlushIfNeeded();
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
                            while (_reader.TryRead(out string? csvLine))
                            {
                                _dataBuffer.Add(csvLine);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                    {
                        // Timeout occurred, continue to check flush conditions
                    }
                }
                catch (OperationCanceledException)
                {
                    // Main cancellation token was cancelled - exit the loop
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in DataFileWriter main loop for {FileName}", _fileName);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
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
            var driveRoot = FindUsbDrive();
            if (driveRoot == null)
            {
                _driveAvailable = false;
                _currentDrivePath = null;
                return;
            }

            _currentDrivePath = driveRoot;
            _logger.LogInformation("Using flash drive: {DrivePath}", driveRoot);
            
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
                _logger.LogInformation("Data will be written to: {FilePath}", _currentFilePath);
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

    private string? FindUsbDrive()
    {
        try
        {
            var mediaDir = "/media";
            if (!Directory.Exists(mediaDir))
            {
                _logger.LogWarning("Media directory {MediaDir} not found - no flash drives available", mediaDir);
                return null;
            }

            var allMountPoints = new List<string>();

            // Look for mounted drives in /media/username/ directories
            var userDirs = Directory.GetDirectories(mediaDir);
            foreach (var userDir in userDirs)
            {
                if (Directory.Exists(userDir))
                {
                    var mountedDrives = Directory.GetDirectories(userDir);
                    allMountPoints.AddRange(mountedDrives);
                }
            }

            // Also check direct mounts in /media (some systems mount directly there)
            var directMounts = Directory.GetDirectories(mediaDir).Where(d => !Directory.GetDirectories(d).Any() || Directory.GetFiles(d).Any()).ToList();
            allMountPoints.AddRange(directMounts);

            // Filter out obvious non-USB paths and validate they're actual mount points
            var validDrives = new List<string>();
            foreach (var drive in allMountPoints.Distinct())
            {
                if (IsValidUsbMount(drive))
                {
                    validDrives.Add(drive);
                }
            }

            if (validDrives.Count == 0)
            {
                _logger.LogWarning("No valid USB drives found in {MediaDir}", mediaDir);
                return null;
            }

            _logger.LogInformation("Found {DriveCount} USB drive(s): {Drives}", validDrives.Count, string.Join(", ", validDrives));

            // Return the first valid drive
            var selectedDrive = validDrives[0];
            _logger.LogInformation("Selected USB drive: {DrivePath}", selectedDrive);
            
            return selectedDrive;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding USB drives");
            return null;
        }
    }

    private bool IsValidUsbMount(string path)
    {
        try
        {
            // Check if directory exists and is accessible
            if (!Directory.Exists(path))
                return false;

            // Try to access the directory (this will fail if it's not a real mount)
            Directory.GetDirectories(path);

            // Check if we can write to it
            var testFile = Path.Combine(path, ".write_test");
            try
            {
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch
            {
                _logger.LogWarning("USB drive {Path} is not writable", path);
                return false;
            }

            // Exclude obvious system directories
            var dirName = Path.GetFileName(path).ToLower();
            if (dirName.Contains("system") || dirName.Contains("boot") || dirName.Contains("root"))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Path {Path} is not a valid USB mount: {Error}", path, ex.Message);
            return false;
        }
    }

    public override void Dispose()
    {
        try
        {
            if (!_writer.TryComplete())
            {
                // Channel was already closed
                _logger.LogDebug("Channel writer was already completed for {FileName}", _fileName);
            }
        }
        catch (InvalidOperationException)
        {
            // Channel was already closed
            _logger.LogDebug("Channel writer was already closed for {FileName}", _fileName);
        }
        
        base.Dispose();
    }
}