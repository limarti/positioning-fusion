using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Backend.Hubs;

namespace Backend.Storage;

public class FileLoggingStatusService : BackgroundService
{
    private readonly ILogger<FileLoggingStatusService> _logger;
    private readonly IHubContext<DataHub> _hubContext;
    private readonly List<DataFileWriter> _fileWriters;

    public FileLoggingStatusService(
        ILogger<FileLoggingStatusService> logger,
        IHubContext<DataHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
        _fileWriters = new List<DataFileWriter>();
    }

    public void RegisterFileWriter(DataFileWriter writer)
    {
        _fileWriters.Add(writer);
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

        // Check USB drive availability
        status.DriveAvailable = CheckDriveAvailability();
        status.DrivePath = GetDrivePath();

        // Get file information for active logging files
        status.ActiveFiles = new List<LoggingFileInfo>();

        if (status.DriveAvailable && !string.IsNullOrEmpty(status.DrivePath))
        {
            try
            {
                var loggingDir = Path.Combine(status.DrivePath, "Logging");
                if (Directory.Exists(loggingDir))
                {
                    // Get current session directory (most recent)
                    var sessionDirs = Directory.GetDirectories(loggingDir)
                        .OrderByDescending(d => Directory.GetCreationTime(d))
                        .Take(1);

                    foreach (var sessionDir in sessionDirs)
                    {
                        status.CurrentSession = Path.GetFileName(sessionDir);

                        // Check for active logging files
                        var files = new[] { "imu.txt", "gnss.raw", "system.txt" };

                        foreach (var fileName in files)
                        {
                            var filePath = Path.Combine(sessionDir, fileName);
                            if (File.Exists(filePath))
                            {
                                var fileInfo = new FileInfo(filePath);
                                status.ActiveFiles.Add(new LoggingFileInfo
                                {
                                    FileName = fileName,
                                    FilePath = filePath,
                                    FileSizeBytes = fileInfo.Length,
                                    LastModified = fileInfo.LastWriteTime,
                                    IsActive = IsFileBeingWritten(filePath)
                                });
                            }
                        }
                    }

                    // Get drive space information
                    var driveInfo = new DriveInfo(status.DrivePath);
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

    private bool CheckDriveAvailability()
    {
        try
        {
            var mediaDir = "/media";
            if (!Directory.Exists(mediaDir))
                return false;

            var drives = Directory.GetDirectories(mediaDir);
            return drives.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private string? GetDrivePath()
    {
        try
        {
            var mediaDir = "/media";
            if (!Directory.Exists(mediaDir))
                return null;

            var drives = Directory.GetDirectories(mediaDir);
            return drives.Length > 0 ? drives[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private bool IsFileBeingWritten(string filePath)
    {
        try
        {
            // Check if file was modified in the last 5 seconds
            var lastWrite = File.GetLastWriteTime(filePath);
            return DateTime.Now - lastWrite < TimeSpan.FromSeconds(5);
        }
        catch
        {
            return false;
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
    public bool IsActive { get; set; }
}