using Backend.Hubs;
using Backend.Storage;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;

namespace Backend.Hardware.Camera;

public class CameraService : BackgroundService, IDisposable
{
    private readonly IHubContext<DataHub> _hubContext;
    private readonly ILogger<CameraService> _logger;
    private readonly CameraInitializer _cameraInitializer;
    private readonly DataFileWriter _dataFileWriter;
    private readonly string _devicePath;
    private readonly TimeSpan _captureInterval = TimeSpan.FromSeconds(5); // Capture every 5 seconds
    private bool _headerWritten = false;
    private DateTime _lastCaptureTime = DateTime.MinValue;
    private int _successfulCaptures = 0;
    private int _failedCaptures = 0;
    private bool _disposed = false;

    public CameraService(
        IHubContext<DataHub> hubContext,
        ILogger<CameraService> logger,
        CameraInitializer cameraInitializer,
        ILoggerFactory loggerFactory)
    {
        _hubContext = hubContext;
        _logger = logger;
        _cameraInitializer = cameraInitializer;
        _dataFileWriter = new DataFileWriter("Camera.txt", loggerFactory.CreateLogger<DataFileWriter>());
        _devicePath = "/dev/video0"; // Default camera device
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Camera Service started - capturing frames every {Interval} seconds", _captureInterval.TotalSeconds);

        // Start the data file writer
        _ = Task.Run(() => _dataFileWriter.StartAsync(stoppingToken), stoppingToken);

        // Check if camera was initialized
        var cameraAvailable = _cameraInitializer.IsCameraAvailable();
        
        if (!cameraAvailable)
        {
            _logger.LogWarning("Camera not available - Camera service will run in disconnected mode");
            
            // Send disconnected status to frontend periodically
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SendCameraDisconnectedUpdate();
                    await Task.Delay(_captureInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error sending camera disconnected status");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
            return;
        }

        _logger.LogInformation("Camera Service connected to video device - capturing at {Interval}s intervals", _captureInterval.TotalSeconds);

        // Main capture loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var captureStartTime = DateTime.UtcNow;
                var stopwatch = Stopwatch.StartNew();

                // Capture frame
                var imageData = await CaptureFrameAsync();
                var captureTimeMs = stopwatch.Elapsed.TotalMilliseconds;

                if (imageData == null || imageData.Length == 0)
                {
                    _failedCaptures++;
                    _logger.LogWarning("Failed to capture frame from camera (Failed captures: {FailedCount})", _failedCaptures);
                    
                    // Send disconnected status
                    await SendCameraDisconnectedUpdate();
                    
                    // Wait before retrying
                    await Task.Delay(_captureInterval, stoppingToken);
                    continue;
                }

                _successfulCaptures++;
                var imageSizeKb = imageData.Length / 1024.0;
                _logger.LogInformation("Frame captured successfully - Size: {SizeKb:F1} KB, Capture time: {CaptureTime:F2}ms (Total successful: {SuccessCount})", 
                    imageSizeKb, captureTimeMs, _successfulCaptures);

                // Image is already JPEG encoded from fswebcam
                var encodingTimeMs = 0.0; // No additional encoding needed

                // Convert to base64
                var base64Image = Convert.ToBase64String(imageData);

                _logger.LogInformation("Frame processed successfully - Size: {SizeKb:F1} KB", imageSizeKb);

                // Create camera data - we'll assume 640x480 since fswebcam captures at this resolution
                var cameraData = new CameraData
                {
                    Timestamp = captureStartTime,
                    ImageBase64 = base64Image,
                    ImageSizeBytes = imageData.Length,
                    ImageWidth = 640,
                    ImageHeight = 480,
                    Format = "JPEG"
                };

                // Write CSV header if this is the first data
                if (!_headerWritten)
                {
                    var csvHeader = "timestamp,width,height,size_bytes,size_kb,capture_time_ms,encoding_time_ms";
                    _dataFileWriter.WriteData(csvHeader);
                    _headerWritten = true;
                    _logger.LogInformation("Camera data CSV header written to file");
                }

                // Log data to file
                var csvLine = $"{cameraData.Timestamp:F2},{cameraData.ImageWidth},{cameraData.ImageHeight},{cameraData.ImageSizeBytes},{imageSizeKb:F1},{captureTimeMs:F2},{encodingTimeMs:F2}";
                _dataFileWriter.WriteData(csvLine);

                // Send data via SignalR
                await SendCameraUpdate(cameraData, captureTimeMs, encodingTimeMs);

                _lastCaptureTime = captureStartTime;

                // Wait for next capture interval
                var totalProcessingTime = TimeSpan.FromMilliseconds(captureTimeMs + encodingTimeMs);
                var remainingWaitTime = _captureInterval - totalProcessingTime;
                
                if (remainingWaitTime > TimeSpan.Zero)
                {
                    _logger.LogDebug("Waiting {WaitTime:F1}s until next capture", remainingWaitTime.TotalSeconds);
                    await Task.Delay(remainingWaitTime, stoppingToken);
                }
                else
                {
                    _logger.LogWarning("Processing time ({ProcessingTime:F2}ms) exceeded capture interval - no wait time", 
                        totalProcessingTime.TotalMilliseconds);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _failedCaptures++;
                _logger.LogError(ex, "Exception in camera capture loop (Failed captures: {FailedCount})", _failedCaptures);
                
                try
                {
                    await Task.Delay(_captureInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        
        _logger.LogInformation("Camera Service stopped - Statistics: {SuccessCount} successful captures, {FailedCount} failed captures", 
            _successfulCaptures, _failedCaptures);
    }

    private async Task SendCameraUpdate(CameraData cameraData, double captureTimeMs, double encodingTimeMs)
    {
        try
        {
            var cameraUpdate = new CameraUpdate
            {
                Timestamp = cameraData.Timestamp,
                ImageBase64 = cameraData.ImageBase64,
                ImageSizeBytes = cameraData.ImageSizeBytes,
                ImageWidth = cameraData.ImageWidth,
                ImageHeight = cameraData.ImageHeight,
                Format = cameraData.Format,
                CaptureTimeMs = captureTimeMs,
                EncodingTimeMs = encodingTimeMs,
                IsConnected = true
            };

            await _hubContext.Clients.All.SendAsync("CameraUpdate", cameraUpdate);
            _logger.LogDebug("Camera update sent via SignalR - {Width}x{Height}, {SizeKb:F1} KB", 
                cameraUpdate.ImageWidth, cameraUpdate.ImageHeight, cameraUpdate.ImageSizeBytes / 1024.0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send camera update via SignalR");
        }
    }

    private async Task SendCameraDisconnectedUpdate()
    {
        try
        {
            var disconnectedUpdate = new CameraUpdate
            {
                Timestamp = DateTime.UtcNow,
                ImageBase64 = string.Empty,
                ImageSizeBytes = 0,
                ImageWidth = 0,
                ImageHeight = 0,
                Format = "NONE",
                CaptureTimeMs = 0,
                EncodingTimeMs = 0,
                IsConnected = false
            };

            await _hubContext.Clients.All.SendAsync("CameraUpdate", disconnectedUpdate);
            _logger.LogDebug("Camera disconnected status sent via SignalR");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send camera disconnected status via SignalR");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Camera Service");
        
        await _dataFileWriter.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task<byte[]?> CaptureFrameAsync()
    {
        try
        {
            // Use fswebcam to capture a frame - this is a lightweight alternative
            // that works well on Raspberry Pi with USB cameras
            var tempFile = Path.GetTempFileName() + ".jpg";
            
            var processInfo = new ProcessStartInfo
            {
                FileName = "fswebcam",
                Arguments = $"--device {_devicePath} --resolution 640x480 --jpeg 85 --no-banner --save {tempFile}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _logger.LogDebug("Executing: fswebcam {Arguments}", processInfo.Arguments);

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start fswebcam process");
                return null;
            }

            // Add timeout for frame capture
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                var output = await process.StandardOutput.ReadToEndAsync();
                _logger.LogError("fswebcam failed with exit code {ExitCode}. Stderr: {Error}. Stdout: {Output}", process.ExitCode, error, output);
                return null;
            }

            // Add a small delay to ensure file is written
            await Task.Delay(100);

            if (File.Exists(tempFile))
            {
                var imageData = await File.ReadAllBytesAsync(tempFile);
                File.Delete(tempFile);
                
                _logger.LogDebug("Successfully captured frame: {Size} bytes from {TempFile}", imageData.Length, tempFile);
                return imageData;
            }
            else
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                _logger.LogError("fswebcam did not create output file: {TempFile}. Process output: {Output}", tempFile, output);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in CaptureFrameAsync");
            return null;
        }
    }

    private async Task<bool> TestCameraAsync()
    {
        try
        {
            // Simple test to see if the camera is accessible
            var processInfo = new ProcessStartInfo
            {
                FileName = "v4l2-ctl",
                Arguments = $"--device={_devicePath} --list-formats",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null) return false;

            // Add timeout for camera test
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(cts.Token);
            
            if (process.ExitCode == 0)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                _logger.LogInformation("Camera formats available: {Output}", output.Trim());
                return true;
            }
            else
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogWarning("Camera test failed: {Error}", error.Trim());
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception testing camera");
            return false;
        }
    }

    public new void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
        base.Dispose();
    }
}