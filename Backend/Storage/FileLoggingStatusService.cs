using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Backend.Hubs;

namespace Backend.Storage;

public class FileLoggingStatusService : BackgroundService
{
    private readonly ILogger<FileLoggingStatusService> _logger;
    private readonly IHubContext<DataHub> _hubContext;
    private readonly DataFileWriter _dataFileWriter;

    public FileLoggingStatusService(
        ILogger<FileLoggingStatusService> logger,
        IHubContext<DataHub> hubContext,
        DataFileWriter dataFileWriter)
    {
        _logger = logger;
        _hubContext = hubContext;
        _dataFileWriter = dataFileWriter;
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

        // Get USB drive information from DataFileWriter
        status.DriveAvailable = _dataFileWriter.IsDriveAvailable;
        status.DrivePath = _dataFileWriter.CurrentDrivePath;

        // Get file information for active logging files
        status.ActiveFiles = new List<LoggingFileInfo>();

        if (status.DriveAvailable && !string.IsNullOrEmpty(status.DrivePath))
        {
            try
            {
                // Use the current session from DataFileWriter if available
                var currentSessionPath = _dataFileWriter.CurrentSessionPath;
                if (!string.IsNullOrEmpty(currentSessionPath) && Directory.Exists(currentSessionPath))
                {
                    status.CurrentSession = Path.GetFileName(currentSessionPath);

                    // Check for active logging files
                    var files = new[] { "imu.txt", "gnss.raw", "system.txt" };

                    foreach (var fileName in files)
                    {
                        var filePath = Path.Combine(currentSessionPath, fileName);
                        if (File.Exists(filePath))
                        {
                            var fileInfo = new FileInfo(filePath);
                            status.ActiveFiles.Add(new LoggingFileInfo
                            {
                                FileName = fileName,
                                FilePath = filePath,
                                FileSizeBytes = fileInfo.Length,
                                LastModified = fileInfo.LastWriteTime
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file logging status");
                status.DriveAvailable = false;
            }
        }

        return status;
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