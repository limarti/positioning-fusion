using Backend.Hubs;
using Backend.Storage;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;

namespace Backend.Hardware.Camera;

public class CameraService : BackgroundService, IDisposable
{
    // Camera configuration constants
    private const int CAMERA_FRAME_RATE = 30;
    private const int CAMERA_WIDTH = 2560;
    private const int CAMERA_HEIGHT = 720;
    private readonly IHubContext<DataHub> _hubContext;
    private readonly ILogger<CameraService> _logger;
    private readonly DataFileWriter _dataFileWriter;
    private readonly DataFileWriter _videoFileWriter;
    private readonly string _devicePath;
    private Process? _ffmpegProcess = null;
    private bool _disposed = false;
    private string _currentVideoFile = string.Empty;

    public CameraService(
        IHubContext<DataHub> hubContext,
        ILogger<CameraService> logger,
        ILoggerFactory loggerFactory)
    {
        _hubContext = hubContext;
        _logger = logger;
        _dataFileWriter = new DataFileWriter("Camera.txt", loggerFactory.CreateLogger<DataFileWriter>());
        _videoFileWriter = new DataFileWriter("Camera.mjpeg", loggerFactory.CreateLogger<DataFileWriter>());
        _devicePath = "/dev/video0"; // Default camera device
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Camera Service started - continuous video recording at {Width}x{Height} {FrameRate}fps", 
            CAMERA_WIDTH, CAMERA_HEIGHT, CAMERA_FRAME_RATE);

        // Start the data file writers for logging
        _ = Task.Run(() => _dataFileWriter.StartAsync(stoppingToken), stoppingToken);
        _ = Task.Run(() => _videoFileWriter.StartAsync(stoppingToken), stoppingToken);

        // Check if camera device exists
        if (!File.Exists(_devicePath))
        {
            _logger.LogWarning("Camera device not found at {DevicePath} - Camera service will run in disconnected mode", _devicePath);
            
            // Send disconnected status to frontend periodically
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SendCameraDisconnectedUpdate();
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
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

        _logger.LogInformation("Camera Service connected to video device - starting continuous recording");

        try
        {
            // Start continuous video recording
            await StartVideoRecording(stoppingToken);
            
            // Send connected status periodically while recording
            while (!stoppingToken.IsCancellationRequested && _ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                try
                {
                    await SendCameraConnectedUpdate();
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error sending camera status");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in camera service");
        }
        finally
        {
            await StopVideoRecording();
        }
        
        _logger.LogInformation("Camera Service stopped");
    }

    private async Task StartVideoRecording(CancellationToken stoppingToken)
    {
        try
        {
            // First configure the camera with v4l2-ctl
            await ConfigureCameraAsync();
            
            var processInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-f v4l2 -input_format mjpeg -video_size {CAMERA_WIDTH}x{CAMERA_HEIGHT} -framerate {CAMERA_FRAME_RATE} -i {_devicePath} -c:v copy -f mjpeg -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _logger.LogInformation("Starting direct camera stream recording from {DevicePath}", _devicePath);

            _ffmpegProcess = Process.Start(processInfo);
            
            if (_ffmpegProcess == null)
            {
                throw new InvalidOperationException("Failed to start camera stream process");
            }

            _logger.LogInformation("Camera stream process started - streaming raw MJPEG data to DataFileWriter");
            
            // Log recording start to batched file writer
            var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_STARTED,{_devicePath},{CAMERA_WIDTH}x{CAMERA_HEIGHT},{CAMERA_FRAME_RATE}fps,raw_mjpeg";
            _dataFileWriter.WriteData(logEntry);

            // Stream data to video file writer (same as GNSS raw data buffering)
            _ = Task.Run(async () =>
            {
                try
                {
                    var buffer = new byte[8192];
                    var stream = _ffmpegProcess.StandardOutput.BaseStream;
                    
                    while (!stoppingToken.IsCancellationRequested && !_ffmpegProcess.HasExited)
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                        if (bytesRead == 0)
                            break;
                            
                        // Write raw camera data to video file (like GNSS writes raw data)
                        _videoFileWriter.WriteData(buffer.Take(bytesRead).ToArray());
                    }
                    
                    _logger.LogInformation("Camera stream recording completed successfully");
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error streaming camera data to file");
                }
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start camera stream recording");
            throw;
        }
    }

    private async Task StopVideoRecording()
    {
        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            try
            {
                _logger.LogInformation("Stopping camera stream recording...");
                
                // Kill the cat process
                _ffmpegProcess.Kill(entireProcessTree: false);
                
                // Wait for process to exit with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _ffmpegProcess.WaitForExitAsync(cts.Token);
                
                _logger.LogInformation("Camera stream recording stopped - file saved: {VideoFile}", _currentVideoFile);
                
                // Log recording stop to batched file writer
                var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_STOPPED,{_devicePath}";
                _dataFileWriter.WriteData(logEntry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping camera stream recording");
            }
            finally
            {
                _ffmpegProcess?.Dispose();
                _ffmpegProcess = null;
            }
        }
    }

    private async Task ConfigureCameraAsync()
    {
        try
        {
            _logger.LogInformation("Configuring camera to {Width}x{Height} MJPEG format", 
                CAMERA_WIDTH, CAMERA_HEIGHT);
            
            var processInfo = new ProcessStartInfo
            {
                FileName = "v4l2-ctl",
                Arguments = $"--device={_devicePath} --set-fmt-video=width={CAMERA_WIDTH},height={CAMERA_HEIGHT},pixelformat=MJPG",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                if (process.ExitCode == 0)
                {
                    _logger.LogInformation("Camera configured successfully: {Output}", output.Trim());
                }
                else
                {
                    _logger.LogWarning("Camera configuration warning: {Error}", error.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to configure camera - continuing anyway");
        }
    }

    private async Task SendCameraConnectedUpdate()
    {
        try
        {
            var connectedUpdate = new CameraUpdate
            {
                Timestamp = DateTime.UtcNow,
                ImageBase64 = string.Empty,
                ImageSizeBytes = 0,
                ImageWidth = CAMERA_WIDTH,
                ImageHeight = CAMERA_HEIGHT,
                Format = "VIDEO_RECORDING",
                CaptureTimeMs = 0,
                EncodingTimeMs = 0,
                IsConnected = true
            };

            await _hubContext.Clients.All.SendAsync("CameraUpdate", connectedUpdate);
            _logger.LogDebug("Camera recording status sent via SignalR");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send camera recording status via SignalR");
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
        await _videoFileWriter.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
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