using System.Threading.Channels;
using System.Text;
using Backend.Configuration;
using Backend.Hardware.Gnss;

namespace Backend.Storage;

public class DataFileWriter : BackgroundService
{
    public const string LoggingDirectoryName = "Logging";
    
    private readonly ILogger<DataFileWriter> _logger;
    private readonly string _fileName;
    private readonly Channel<object> _dataChannel;
    private readonly ChannelWriter<object> _writer;
    private readonly ChannelReader<object> _reader;

    private readonly List<object> _dataBuffer = new();
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

    // Static properties for sharing drive information across services
    public static string? SharedDrivePath { get; private set; }
    public static string? SharedSessionPath { get; private set; }
    public static bool SharedDriveAvailable { get; private set; }

    // Session counter and rename tracking
    private static int _sessionCounter = -1;
    private static bool _sessionRenamed = false;
    private static DateTime _sessionStartTime = DateTime.UtcNow;
    private static readonly object _renameLock = new object();
    private const string SessionCounterFileName = ".session_counter";

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

        _dataChannel = Channel.CreateBounded<object>(options);
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

    public void WriteData(byte[] binaryData)
    {
        if (!_writer.TryWrite(binaryData))
        {
            _logger.LogWarning("Failed to queue binary data for {FileName} - channel full", _fileName);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataFileWriter started for {FileName}", _fileName);

        // Initialize USB drive detection and session path immediately at startup
        EnsureSessionPathExists();

        var lastFlushTime = DateTime.UtcNow;
        var lastGnssTimeCheck = DateTime.UtcNow;
        const int flushIntervalMs = 1000; // 1 second
        const int gnssTimeCheckIntervalSeconds = 15; // Check GNSS time every 15 seconds

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

                    // Check for rename opportunities every 15 seconds
                    var timeSinceLastGnssCheck = DateTime.UtcNow - lastGnssTimeCheck;
                    if (timeSinceLastGnssCheck.TotalSeconds >= gnssTimeCheckIntervalSeconds && !_sessionRenamed)
                    {
                        // Try to rename the session folder using GNSS time
                        var gnssTime = GnssService.GetLastValidGnssTime();
                        if (gnssTime.HasValue)
                        {
                            _logger.LogInformation("Valid GNSS time detected: {GnssTime}, renaming session folder", gnssTime.Value);
                            TryRenameSessionFolder(gnssTime.Value);
                        }

                        lastGnssTimeCheck = DateTime.UtcNow;
                    }

                    // Wait for data or timeout
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeoutCts.CancelAfter(timeUntilNextFlush);

                    try
                    {
                        if (await _reader.WaitToReadAsync(timeoutCts.Token))
                        {
                            // Process incoming data
                            while (_reader.TryRead(out object? data))
                            {
                                _dataBuffer.Add(data);
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
        // Check for USB drive periodically if not available
        if (!_driveAvailable)
        {
            EnsureSessionPathExists();
        }
        // Also check if shared session path has changed (from rename)
        else if (!string.IsNullOrEmpty(SharedSessionPath) && SharedSessionPath != _currentSessionPath)
        {
            // Session was renamed, update our paths
            if (Directory.Exists(SharedSessionPath))
            {
                _currentSessionPath = SharedSessionPath;
                _currentFilePath = Path.Combine(SharedSessionPath, _fileName);
                _logger.LogDebug("Updated paths after session rename: {Path}", _currentFilePath);
            }
        }

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
            // Check if drive is available before writing
            if (!_driveAvailable || string.IsNullOrEmpty(_currentFilePath))
            {
                _logger.LogWarning("No USB drive available - keeping {Count} data items buffered for {FileName}", _dataBuffer.Count, _fileName);
                return;
            }

            // Verify drive is still accessible before writing
            if (!Directory.Exists(Path.GetDirectoryName(_currentFilePath)))
            {
                _logger.LogWarning("USB drive disconnected during operation - buffering data for {FileName}", _fileName);
                HandleDriveDisconnection();
                return;
            }

            // Check if we have text or binary data
            bool hasBinaryData = _dataBuffer.Any(item => item is byte[]);
            
            if (hasBinaryData)
            {
                // Write binary data
                using var fileStream = new FileStream(_currentFilePath, FileMode.Append, FileAccess.Write);
                foreach (var item in _dataBuffer)
                {
                    if (item is byte[] binaryData)
                    {
                        await fileStream.WriteAsync(binaryData);
                    }
                    else if (item is string textData)
                    {
                        var textBytes = Encoding.UTF8.GetBytes(textData + Environment.NewLine);
                        await fileStream.WriteAsync(textBytes);
                    }
                }
            }
            else
            {
                // Write text data
                var textContent = new StringBuilder();

                // Add all buffered data
                foreach (var item in _dataBuffer)
                {
                    if (item is string line)
                    {
                        textContent.AppendLine(line);
                    }
                }

                await File.AppendAllTextAsync(_currentFilePath, textContent.ToString());
            }

            _logger.LogDebug("Flushed {Count} items to {FilePath}", _dataBuffer.Count, _currentFilePath);

            _dataBuffer.Clear();
            _lastFlush = DateTime.UtcNow;
        }
        catch (DirectoryNotFoundException)
        {
            _logger.LogWarning("USB drive disconnected - directory not found for {FileName}", _fileName);
            HandleDriveDisconnection();
        }
        catch (DriveNotFoundException)
        {
            _logger.LogWarning("USB drive disconnected - drive not found for {FileName}", _fileName);
            HandleDriveDisconnection();
        }
        catch (IOException ex) when (ex.Message.Contains("device") || ex.Message.Contains("drive"))
        {
            _logger.LogWarning("USB drive disconnected - IO error for {FileName}: {Error}", _fileName, ex.Message);
            HandleDriveDisconnection();
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("USB drive disconnected - access denied for {FileName}", _fileName);
            HandleDriveDisconnection();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing data buffer for {FileName}", _fileName);
        }
    }

    private void HandleDriveDisconnection()
    {
        if (_driveAvailable)
        {
            _logger.LogWarning("USB drive disconnected - switching to buffer mode for {FileName}", _fileName);
            _driveAvailable = false;
            _currentDrivePath = null;
            _currentFilePath = null;
            _currentSessionPath = null;

            // Update shared static properties to reflect disconnection
            SharedDriveAvailable = false;
            SharedDrivePath = null;
            SharedSessionPath = null;
        }
    }

    private void EnsureSessionPathExists()
    {
        try
        {
            // Only detect USB drive if we don't have one cached or it's not available
            if (_currentDrivePath == null || !_driveAvailable)
            {
                var driveRoot = FindUsbDrive();
                if (driveRoot == null)
                {
                    _driveAvailable = false;
                    _currentDrivePath = null;
                    return;
                }

                _currentDrivePath = driveRoot;
                _logger.LogInformation("USB drive detected: {DrivePath}", driveRoot);
            }

            var loggingDir = Path.Combine(_currentDrivePath, LoggingDirectoryName);

            // Initialize session counter on first run (app startup)
            if (_sessionCounter == -1)
            {
                // App just started - read and increment counter for new session
                _sessionCounter = ReadOrCreateSessionCounter(loggingDir);
                WriteSessionCounter(loggingDir, _sessionCounter);
                _sessionStartTime = DateTime.UtcNow;
                _sessionRenamed = false;
                _logger.LogInformation("Initializing new session {SessionNumber} on application startup", _sessionCounter);
            }

            // Create or reuse session directory
            var sessionFolder = $"session_{_sessionCounter:D5}";
            var sessionPath = Path.Combine(loggingDir, sessionFolder);
            
            // Only create/set paths if not already set or path changed
            if (string.IsNullOrEmpty(_currentSessionPath) || _currentSessionPath != sessionPath)
            {
                // Check if session was renamed (look in SharedSessionPath)
                if (!string.IsNullOrEmpty(SharedSessionPath) && 
                    Directory.Exists(SharedSessionPath) && 
                    !SharedSessionPath.Contains("session_"))
                {
                    // Session was already renamed, use the renamed path
                    _currentSessionPath = SharedSessionPath;
                    _currentFilePath = Path.Combine(SharedSessionPath, _fileName);
                    _sessionRenamed = true;
                    _logger.LogDebug("Using renamed session: {SessionPath}", SharedSessionPath);
                }
                else
                {
                    // Use the session_XXXXX path
                    if (!Directory.Exists(sessionPath))
                    {
                        Directory.CreateDirectory(sessionPath);
                        _logger.LogInformation("Created new logging session: {SessionPath}", sessionPath);
                    }
                    else
                    {
                        _logger.LogDebug("Resuming logging to existing session: {SessionPath}", sessionPath);
                    }
                    
                    _currentSessionPath = sessionPath;
                    _currentFilePath = Path.Combine(sessionPath, _fileName);
                    _logger.LogInformation("Data will be written to: {FilePath}", _currentFilePath);
                }
            }

            _driveAvailable = true;

            // Update shared static properties
            SharedDrivePath = _currentDrivePath;
            SharedSessionPath = _currentSessionPath;
            SharedDriveAvailable = _driveAvailable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring session path exists");
            _driveAvailable = false;

            // Update shared static properties
            SharedDriveAvailable = false;
        }

    }

    private int GetBufferSizeInBytes()
    {
        return _dataBuffer.Sum(item => item switch
        {
            string line => Encoding.UTF8.GetByteCount(line + Environment.NewLine),
            byte[] binaryData => binaryData.Length,
            _ => 0
        });
    }


    private string? FindUsbDrive()
    {
        try
        {
            // Read /proc/mounts to find actual mounted filesystems
            var mountsFile = "/proc/mounts";
            if (!File.Exists(mountsFile))
            {
                _logger.LogWarning("Cannot access {MountsFile} - no USB drive detection available", mountsFile);
                return null;
            }

            var mountLines = File.ReadAllLines(mountsFile);
            var validDrives = new List<string>();

            foreach (var line in mountLines)
            {
                var parts = line.Split(' ');
                if (parts.Length < 3)
                    continue;

                var device = parts[0];
                var mountPoint = parts[1];
                var fileSystem = parts[2];

                // Only consider removable storage devices mounted in /media
                if (mountPoint.StartsWith("/media/") && IsRemovableStorageDevice(device, fileSystem))
                {
                    if (IsValidUsbMount(mountPoint))
                    {
                        validDrives.Add(mountPoint);
                        _logger.LogDebug("Found valid USB mount: {Device} -> {MountPoint} ({FileSystem})", device, mountPoint, fileSystem);
                    }
                }
            }

            if (validDrives.Count == 0)
            {
                _logger.LogDebug("No valid USB drives found in /proc/mounts");
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

    private bool IsRemovableStorageDevice(string device, string fileSystem)
    {
        // Check for common removable storage filesystem types
        var removableFileSystems = new[] { "vfat", "fat32", "exfat", "ntfs", "ext4", "ext3", "ext2" };
        if (!removableFileSystems.Contains(fileSystem.ToLower()))
            return false;

        // Skip obvious system devices
        if (device.StartsWith("/dev/loop") || 
            device.StartsWith("/dev/sr") || 
            device.Contains("snap") ||
            device.StartsWith("/dev/mapper"))
            return false;

        // Look for USB/removable storage patterns
        return device.StartsWith("/dev/sd") || device.StartsWith("/dev/mmcblk") || device.StartsWith("/dev/nvme");
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

    private int ReadOrCreateSessionCounter(string loggingDir)
    {
        var counterFilePath = Path.Combine(loggingDir, SessionCounterFileName);

        try
        {
            if (File.Exists(counterFilePath))
            {
                var content = File.ReadAllText(counterFilePath).Trim();
                if (int.TryParse(content, out int counter))
                {
                    _logger.LogInformation("Read session counter: {Counter} from {FilePath}", counter, counterFilePath);
                    return counter + 1; // Increment for new session
                }
                else
                {
                    _logger.LogWarning("Invalid session counter file content, starting from 1");
                    return 1;
                }
            }
            else
            {
                _logger.LogInformation("No session counter file found, starting from 1");
                return 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading session counter file, starting from 1");
            return 1;
        }
    }

    private void WriteSessionCounter(string loggingDir, int counter)
    {
        var counterFilePath = Path.Combine(loggingDir, SessionCounterFileName);

        try
        {
            File.WriteAllText(counterFilePath, counter.ToString());
            _logger.LogInformation("Wrote session counter: {Counter} to {FilePath}", counter, counterFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing session counter file");
        }
    }

    // Removed FindExistingSessionPath - no longer needed

    private void TryRenameSessionFolder(DateTime gnssTime)
    {
        lock (_renameLock)
        {
            // Check if already renamed (by any writer)
            if (_sessionRenamed)
                return;

            // Check if another writer already renamed it
            if (!string.IsNullOrEmpty(SharedSessionPath) &&
                !SharedSessionPath.Contains("session_") &&
                Directory.Exists(SharedSessionPath))
            {
                // Another writer already renamed it, just update our paths
                _currentSessionPath = SharedSessionPath;
                _currentFilePath = Path.Combine(SharedSessionPath, _fileName);
                _sessionRenamed = true;
                _logger.LogDebug("Session already renamed by another writer: {Path}", SharedSessionPath);
                return;
            }

            // Check if we have a session path to rename
            if (string.IsNullOrEmpty(_currentSessionPath) || !Directory.Exists(_currentSessionPath))
            {
                _logger.LogWarning("Cannot rename session - no valid session path exists");
                return;
            }

            try
            {
                // Create new folder name
                var parentDir = Path.GetDirectoryName(_currentSessionPath);
                if (string.IsNullOrEmpty(parentDir))
                {
                    _logger.LogError("Cannot determine parent directory of session path");
                    return;
                }

                // GNSS time format: yyyy-MM-dd-HH-mm
                var newFolderName = gnssTime.ToString("yyyy-MM-dd-HH-mm");
                var newSessionPath = Path.Combine(parentDir, newFolderName);

                // Check if target already exists - add counter if needed
                int counter = 1;
                var finalPath = newSessionPath;
                while (Directory.Exists(finalPath))
                {
                    finalPath = $"{newSessionPath}-{counter:D2}";
                    counter++;
                    if (counter > 99)
                    {
                        _logger.LogError("Too many duplicate session folders");
                        return;
                    }
                }

                // Perform the rename
                Directory.Move(_currentSessionPath, finalPath);

                _logger.LogInformation("Successfully renamed session folder: {OldPath} → {NewPath}",
                    _currentSessionPath, finalPath);

                // Update all paths
                _currentSessionPath = finalPath;
                _currentFilePath = Path.Combine(finalPath, _fileName);
                
                // Update shared properties so other writers can see the change
                SharedSessionPath = finalPath;
                
                // Mark as renamed
                _sessionRenamed = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming session folder");
            }
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