using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Backend.Hubs;

namespace Backend.Storage;

public class FileLoggingStatusService : BackgroundService
{
    private readonly ILogger<FileLoggingStatusService> _logger;
    private readonly IHubContext<DataHub> _hubContext;

    public FileLoggingStatusService(
        ILogger<FileLoggingStatusService> logger,
        IHubContext<DataHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("File logging status service started");

        var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var status = await GetFileLoggingStatus();
                await _hubContext.Clients.All.SendAsync("FileLoggingStatusUpdate", status, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending file logging status update");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }

        _logger.LogInformation("File logging status service stopped");
    }

    private async Task<FileLoggingStatus> GetFileLoggingStatus()
    {
        var status = new FileLoggingStatus();

        // Get USB drive information from shared DataFileWriter properties
        status.DriveAvailable = DataFileWriter.SharedDriveAvailable;
        status.DrivePath = DataFileWriter.SharedDrivePath;

        // Get file information for active logging files
        status.ActiveFiles = new List<LoggingFileInfo>();

        if (status.DriveAvailable && !string.IsNullOrEmpty(status.DrivePath))
        {
            try
            {
                // Use the current session from shared DataFileWriter properties
                var currentSessionPath = DataFileWriter.SharedSessionPath;
                if (!string.IsNullOrEmpty(currentSessionPath) && Directory.Exists(currentSessionPath))
                {
                    status.CurrentSession = Path.GetFileName(currentSessionPath);

                    // Check for all files in the current session directory
                    var allFiles = Directory.GetFiles(currentSessionPath);
                    foreach (var filePath in allFiles)
                    {
                        var fileInfo = new FileInfo(filePath);
                        status.ActiveFiles.Add(new LoggingFileInfo
                        {
                            FileName = fileInfo.Name,
                            FilePath = filePath,
                            FileSizeBytes = fileInfo.Length,
                            LastModified = fileInfo.LastWriteTime
                        });
                    }
                }

                // Get drive space information
                var driveInfo = new DriveInfo(status.DrivePath);
                status.TotalSpaceBytes = driveInfo.TotalSize;
                status.AvailableSpaceBytes = driveInfo.AvailableFreeSpace;
                status.UsedSpaceBytes = status.TotalSpaceBytes - status.AvailableSpaceBytes;

                // Check if storage usage exceeds 80% and perform cleanup if needed
                var storageUsagePercent = (double)status.UsedSpaceBytes / status.TotalSpaceBytes * 100;
                if (storageUsagePercent > 80.0)
                {
                    _logger.LogWarning("Storage usage at {UsagePercent:F1}% - triggering log rotation", storageUsagePercent);
                    await PerformLogRotation(status.DrivePath);
                    
                    // Recalculate storage after cleanup
                    driveInfo = new DriveInfo(status.DrivePath);
                    status.TotalSpaceBytes = driveInfo.TotalSize;
                    status.AvailableSpaceBytes = driveInfo.AvailableFreeSpace;
                    status.UsedSpaceBytes = status.TotalSpaceBytes - status.AvailableSpaceBytes;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file logging status");
                status.DriveAvailable = false;
            }
        }

        return status;
    }

    private Task PerformLogRotation(string drivePath)
    {
        try
        {
            var loggingDir = Path.Combine(drivePath, DataFileWriter.LoggingDirectoryName);
            if (!Directory.Exists(loggingDir))
            {
                _logger.LogWarning("Logging directory not found: {LoggingDir}", loggingDir);
                return Task.CompletedTask;
            }

            // Get all session directories sorted by creation time (oldest first)
            var sessionDirs = Directory.GetDirectories(loggingDir)
                .Select(dir => new DirectoryInfo(dir))
                .Where(dirInfo => IsSessionDirectory(dirInfo.Name))
                .OrderBy(dirInfo => dirInfo.CreationTime)
                .ToList();

            if (sessionDirs.Count <= 1)
            {
                _logger.LogInformation("Only {SessionCount} session(s) found - skipping rotation", sessionDirs.Count);
                return Task.CompletedTask;
            }

            // Get current session to avoid deleting it
            var currentSessionPath = DataFileWriter.SharedSessionPath;
            var currentSessionName = !string.IsNullOrEmpty(currentSessionPath) 
                ? Path.GetFileName(currentSessionPath) 
                : null;

            // Delete oldest sessions until we're under 75% usage or only current session remains
            var driveInfo = new DriveInfo(drivePath);
            var targetUsagePercent = 75.0;

            foreach (var sessionDir in sessionDirs)
            {
                // Skip current session
                if (sessionDir.Name == currentSessionName)
                {
                    _logger.LogDebug("Skipping current session: {SessionName}", sessionDir.Name);
                    continue;
                }

                // Calculate current usage
                driveInfo = new DriveInfo(drivePath);
                var currentUsagePercent = (double)(driveInfo.TotalSize - driveInfo.AvailableFreeSpace) / driveInfo.TotalSize * 100;

                if (currentUsagePercent <= targetUsagePercent)
                {
                    _logger.LogInformation("Storage usage now at {UsagePercent:F1}% - stopping rotation", currentUsagePercent);
                    break;
                }

                // Calculate session size before deletion
                var sessionSize = GetDirectorySize(sessionDir.FullName);
                
                _logger.LogInformation("Deleting oldest session: {SessionName} ({SizeMB:F1} MB)", 
                    sessionDir.Name, sessionSize / (1024.0 * 1024.0));

                // Delete the session directory
                Directory.Delete(sessionDir.FullName, recursive: true);
                
                _logger.LogInformation("Successfully deleted session: {SessionName}", sessionDir.Name);
            }

            // Final storage check
            driveInfo = new DriveInfo(drivePath);
            var finalUsagePercent = (double)(driveInfo.TotalSize - driveInfo.AvailableFreeSpace) / driveInfo.TotalSize * 100;
            _logger.LogInformation("Log rotation completed - storage usage now at {UsagePercent:F1}%", finalUsagePercent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing log rotation");
        }
        
        return Task.CompletedTask;
    }

    private bool IsSessionDirectory(string dirName)
    {
        // Session directories follow pattern: yyyy-MM-dd-HH-mm
        return DateTime.TryParseExact(dirName, "yyyy-MM-dd-HH-mm", null, global::System.Globalization.DateTimeStyles.None, out _);
    }

    private long GetDirectorySize(string directoryPath)
    {
        try
        {
            return Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating directory size for {DirectoryPath}", directoryPath);
            return 0;
        }
    }



}

public class FileLoggingStatus
{
    public bool DriveAvailable { get; set; }
    public string? DrivePath { get; set; }
    public string? CurrentSession { get; set; }
    public long TotalSpaceBytes { get; set; }
    public long AvailableSpaceBytes { get; set; }
    public long UsedSpaceBytes { get; set; }
    public List<LoggingFileInfo> ActiveFiles { get; set; } = new();
}

public class LoggingFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime LastModified { get; set; }
}