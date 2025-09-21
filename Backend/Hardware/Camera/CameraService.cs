using Backend.Hubs;
using Backend.Storage;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;

namespace Backend.Hardware.Camera;

public class CameraService : BackgroundService, IDisposable
{
    // Camera configuration constants
    private const int CAMERA_CAPTURE_RATE = 60;  // Hardware capture rate
    private const int CAMERA_OUTPUT_RATE = 15;   // Desired output rate (configurable)
    private const int CAMERA_WIDTH = 2560;
    private const int CAMERA_HEIGHT = 720;
    private const int VIDEO_SEGMENT_SECONDS = 60; // Time limit per video file
    private const int FRAME_DROP_RATIO = CAMERA_CAPTURE_RATE / CAMERA_OUTPUT_RATE; // Keep every Nth frame
    private readonly IHubContext<DataHub> _hubContext;
    private readonly ILogger<CameraService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly DataFileWriter _dataFileWriter;
    private DataFileWriter? _currentVideoFileWriter;
    private readonly string _devicePath;
    private Process? _ffmpegProcess = null;
    private bool _disposed = false;
    private int _currentSegmentNumber = 1;
    private DateTime _currentSegmentStartTime = DateTime.UtcNow;

    public CameraService(
        IHubContext<DataHub> hubContext,
        ILogger<CameraService> logger,
        ILoggerFactory loggerFactory)
    {
        _hubContext = hubContext;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _dataFileWriter = new DataFileWriter("Camera.txt", loggerFactory.CreateLogger<DataFileWriter>());
        // Video file writer will be created per segment
        _devicePath = "/dev/video0"; // Default camera device
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Camera Service started - continuous video recording at {Width}x{Height} capture:{CaptureRate}fps output:{OutputRate}fps", 
            CAMERA_WIDTH, CAMERA_HEIGHT, CAMERA_CAPTURE_RATE, CAMERA_OUTPUT_RATE);

        // Start the data file writer for logging
        _ = Task.Run(() => _dataFileWriter.StartAsync(stoppingToken), stoppingToken);

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

            // Stream MJPEG to stdout for frame processing
            var processInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel warning " +
                           $"-f v4l2 -input_format mjpeg -video_size {CAMERA_WIDTH}x{CAMERA_HEIGHT} -framerate {CAMERA_CAPTURE_RATE} -i {_devicePath} " +
                           "-c copy -f mjpeg -",  // Stream raw MJPEG to stdout
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };


            _logger.LogInformation("Starting MJPEG stream capture from {DevicePath}", _devicePath);

            _ffmpegProcess = Process.Start(processInfo);
            
            if (_ffmpegProcess == null)
            {
                throw new InvalidOperationException("Failed to start camera stream process");
            }

            _logger.LogInformation("Camera stream process started - processing MJPEG frames with {DropRatio}:1 frame dropping", FRAME_DROP_RATIO);
            
            // Log recording start to batched file writer
            var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_STARTED,{_devicePath},{CAMERA_WIDTH}x{CAMERA_HEIGHT},capture:{CAMERA_CAPTURE_RATE}fps,output:{CAMERA_OUTPUT_RATE}fps";
            _dataFileWriter.WriteData(logEntry);

            // Process MJPEG stream with frame dropping
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessMjpegStreamWithFrameDropping(stoppingToken);
                    _logger.LogInformation("MJPEG stream processing completed successfully");
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing MJPEG stream");
                }
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start camera stream recording");
            throw;
        }
    }

    private async Task ProcessMjpegStreamWithFrameDropping(CancellationToken stoppingToken)
    {
        if (_ffmpegProcess == null)
            return;

        var stream = _ffmpegProcess.StandardOutput.BaseStream;
        var buffer = new byte[64 * 1024]; // 64KB buffer for reading
        var frameBuffer = new List<byte>();
        
        int frameCount = 0;
        int framesKeptInSegment = 0;
        DateTime lastSegmentTime = DateTime.UtcNow;
        
        // Start first video segment
        await StartNewVideoSegment(stoppingToken);
        
        // MJPEG frame markers
        var startMarker = new byte[] { 0xFF, 0xD8 }; // JPEG start
        var endMarker = new byte[] { 0xFF, 0xD9 };   // JPEG end
        
        _logger.LogInformation("Starting MJPEG frame processing loop...");
        
        while (!stoppingToken.IsCancellationRequested && !_ffmpegProcess.HasExited)
        {
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
            if (bytesRead == 0)
            {
                _logger.LogWarning("FFmpeg stream ended - no more data available");
                break;
            }

            // Debug log data flow every 100KB
            if (frameCount == 0 || (frameCount % 100 == 0))
            {
                _logger.LogDebug("Read {BytesRead} bytes from FFmpeg stream, frame buffer size: {BufferSize}", bytesRead, frameBuffer.Count);
            }

            // Add bytes to frame buffer
            for (int i = 0; i < bytesRead; i++)
            {
                frameBuffer.Add(buffer[i]);
                
                // Check for JPEG end marker
                if (frameBuffer.Count >= 2 && 
                    frameBuffer[frameBuffer.Count - 2] == 0xFF && 
                    frameBuffer[frameBuffer.Count - 1] == 0xD9)
                {
                    // Complete frame found
                    if (ShouldKeepFrame(frameCount))
                    {
                        // Write frame immediately to current video file
                        var frameData = frameBuffer.ToArray();
                        _currentVideoFileWriter?.WriteData(frameData);
                        framesKeptInSegment++;
                        
                        // Debug log every 30 frames to see if data is flowing
                        if (frameCount % 30 == 0)
                        {
                            _logger.LogDebug("Processed {FrameCount} total frames, kept {KeptCount}, current frame size: {FrameSize} bytes", 
                                frameCount, framesKeptInSegment, frameData.Length);
                        }
                        
                        // Check if we need to start a new video segment
                        var timeSinceLastSegment = DateTime.UtcNow - lastSegmentTime;
                        if (timeSinceLastSegment.TotalSeconds >= VIDEO_SEGMENT_SECONDS)
                        {
                            // Complete current segment and start new one
                            await CompleteCurrentSegment(framesKeptInSegment);
                            await StartNewVideoSegment(stoppingToken);
                            
                            lastSegmentTime = DateTime.UtcNow;
                            framesKeptInSegment = 0;
                        }
                    }
                    
                    frameCount++;
                    frameBuffer.Clear();
                }
            }
        }
        
        // Complete the final segment
        await CompleteCurrentSegment(framesKeptInSegment);
    }
    
    private async Task StartNewVideoSegment(CancellationToken stoppingToken)
    {
        _currentSegmentStartTime = DateTime.UtcNow;
        
        // Create new video file for this segment
        var segmentFileName = $"Camera_{_currentSegmentNumber:D3}.mjpeg";
        _currentVideoFileWriter = new DataFileWriter(segmentFileName, _loggerFactory.CreateLogger<DataFileWriter>());
        
        // Start the new video file writer
        _ = Task.Run(() => _currentVideoFileWriter.StartAsync(stoppingToken), stoppingToken);
        
        // Wait a moment for the writer to initialize
        await Task.Delay(100, stoppingToken);
        
        var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},SEGMENT_STARTED,segment:{_currentSegmentNumber},file:{segmentFileName}";
        _dataFileWriter.WriteData(logEntry);
        
        _logger.LogInformation("Started video segment {SegmentNumber}: {FileName}", _currentSegmentNumber, segmentFileName);
    }
    
    private async Task CompleteCurrentSegment(int framesKept)
    {
        var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},SEGMENT_COMPLETED,segment:{_currentSegmentNumber},frames_kept:{framesKept}";
        _dataFileWriter.WriteData(logEntry);
        
        _logger.LogInformation("Completed video segment {SegmentNumber} with {FramesKept} frames", _currentSegmentNumber, framesKept);
        
        // Stop and dispose current video file writer
        if (_currentVideoFileWriter != null)
        {
            await _currentVideoFileWriter.StopAsync(CancellationToken.None);
            _currentVideoFileWriter.Dispose();
            _currentVideoFileWriter = null;
        }
        
        _currentSegmentNumber++;
    }
    
    private static bool ShouldKeepFrame(int frameCount)
    {
        // Keep every FRAME_DROP_RATIO frame (e.g., every 2nd frame for 15fps from 30fps)
        return frameCount % FRAME_DROP_RATIO == 0;
    }

    private async Task StopVideoRecording()
    {
        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            try
            {
                _logger.LogInformation("Stopping MJPEG stream capture...");
                
                // Kill the FFmpeg process
                _ffmpegProcess.Kill(entireProcessTree: false);
                
                // Wait for process to exit with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _ffmpegProcess.WaitForExitAsync(cts.Token);
                
                _logger.LogInformation("MJPEG stream capture stopped");
                
                // Log recording stop to batched file writer
                var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_STOPPED,{_devicePath}";
                _dataFileWriter.WriteData(logEntry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping MJPEG stream capture");
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
        
        // Stop current video file writer if it exists
        if (_currentVideoFileWriter != null)
        {
            await _currentVideoFileWriter.StopAsync(cancellationToken);
            _currentVideoFileWriter.Dispose();
            _currentVideoFileWriter = null;
        }
        
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