using Backend.Hubs;
using Backend.Storage;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;

namespace Backend.Hardware.Camera;

public class CameraService : BackgroundService, IDisposable
{
    // Camera configuration constants
    private const int CAMERA_CAPTURE_RATE = 100;  // Hardware capture rate
    private const int CAMERA_FRAME_MULTIPLIER = 7;   // Number of frames until we collect a frame (configurable)
    private const int CAMERA_WIDTH = 1600;
    private const int CAMERA_HEIGHT = 600;
    private const int VIDEO_SEGMENT_SECONDS = 60; // Time limit per video file
    private const int FRAME_DROP_RATIO = CAMERA_FRAME_MULTIPLIER; // Keep every Nth frame
    private const int DEVICE_CHECK_INTERVAL_SECONDS = 5; // Check for camera every 5 seconds
    private const int CONNECTION_RETRY_DELAY_SECONDS = 2; // Wait between connection attempts
    // Camera connection states
    private enum CameraConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Recording
    }

    private readonly IHubContext<DataHub> _hubContext;
    private readonly ILogger<CameraService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly DataFileWriter _dataFileWriter;
    private string? _currentVideoFilePath;
    private Process? _currentMkvProcess = null;
    private readonly string _devicePath;
    private Process? _ffmpegProcess = null;
    private bool _disposed = false;
    private int _currentSegmentNumber = 1;
    private DateTime _currentSegmentStartTime = DateTime.UtcNow;
    private readonly List<byte[]> _frameBuffer = new(); // Buffer frames for writing to MKV
    private CameraConnectionState _connectionState = CameraConnectionState.Disconnected;
    private DateTime _lastDeviceCheck = DateTime.MinValue;
    private int _connectionAttempts = 0;
    private DateTime _lastConnectionAttempt = DateTime.MinValue;

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
        _logger.LogInformation("Camera Service started with dynamic connection handling - {Width}x{Height} capture:{CaptureRate}fps multiplier:{Multiplier}", 
            CAMERA_WIDTH, CAMERA_HEIGHT, CAMERA_CAPTURE_RATE, CAMERA_FRAME_MULTIPLIER);

        // Start the data file writer for logging
        _ = Task.Run(() => _dataFileWriter.StartAsync(stoppingToken), stoppingToken);

        // Main monitoring loop - continuously check for camera availability
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorCameraConnection(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); // Main loop delay
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in camera monitoring loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        
        // Cleanup on shutdown
        await CleanupCameraResources();
        _logger.LogInformation("Camera Service stopped");
    }

    private async Task MonitorCameraConnection(CancellationToken stoppingToken)
    {
        var currentTime = DateTime.UtcNow;
        
        // Check device availability periodically
        if (currentTime - _lastDeviceCheck >= TimeSpan.FromSeconds(DEVICE_CHECK_INTERVAL_SECONDS))
        {
            _lastDeviceCheck = currentTime;
            var deviceExists = File.Exists(_devicePath);
            
            _logger.LogDebug("Camera device check: {DevicePath} exists={DeviceExists}, current state={State}", 
                _devicePath, deviceExists, _connectionState);

            switch (_connectionState)
            {
                case CameraConnectionState.Disconnected:
                    if (deviceExists)
                    {
                        _logger.LogInformation("Camera device detected at {DevicePath} - attempting connection", _devicePath);
                        _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},DEVICE_DETECTED,{_devicePath}");
                        await AttemptCameraConnection(stoppingToken);
                    }
                    else
                    {
                        await SendCameraDisconnectedUpdate();
                    }
                    break;

                case CameraConnectionState.Connected:
                case CameraConnectionState.Recording:
                    if (!deviceExists)
                    {
                        _logger.LogWarning("Camera device {DevicePath} disappeared - handling disconnection", _devicePath);
                        _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},DEVICE_DISAPPEARED,{_devicePath}");
                        await HandleCameraDisconnection();
                    }
                    else if (_connectionState == CameraConnectionState.Connected)
                    {
                        // Device exists and we're connected but not recording - start recording
                        await StartVideoRecording(stoppingToken);
                    }
                    else if (_connectionState == CameraConnectionState.Recording)
                    {
                        // Check if FFmpeg process is still running
                        if (_ffmpegProcess == null || _ffmpegProcess.HasExited)
                        {
                            _logger.LogWarning("FFmpeg process died unexpectedly - attempting to restart recording");
                            _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},FFMPEG_PROCESS_DIED,exit_code:{_ffmpegProcess?.ExitCode ?? -1}");
                            await HandleCameraDisconnection();
                            await AttemptCameraConnection(stoppingToken);
                        }
                    }
                    break;

                case CameraConnectionState.Connecting:
                    // Allow connection attempt to complete
                    break;
            }
        }
    }

    private async Task AttemptCameraConnection(CancellationToken stoppingToken)
    {
        var currentTime = DateTime.UtcNow;
        
        // Prevent too frequent connection attempts
        if (currentTime - _lastConnectionAttempt < TimeSpan.FromSeconds(CONNECTION_RETRY_DELAY_SECONDS))
        {
            return;
        }

        _connectionState = CameraConnectionState.Connecting;
        _lastConnectionAttempt = currentTime;
        _connectionAttempts++;

        try
        {
            _logger.LogInformation("Camera connection attempt #{AttemptNumber} to {DevicePath}", _connectionAttempts, _devicePath);
            _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},CONNECTION_ATTEMPT,attempt:{_connectionAttempts},device:{_devicePath}");

            // Test camera configuration first
            await ConfigureCameraAsync();
            
            _connectionState = CameraConnectionState.Connected;
            _connectionAttempts = 0; // Reset on successful connection
            
            _logger.LogInformation("Camera successfully connected to {DevicePath}", _devicePath);
            _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},CONNECTION_SUCCESS,device:{_devicePath}");
            
            await SendCameraConnectedStatus();
        }
        catch (Exception ex)
        {
            _connectionState = CameraConnectionState.Disconnected;
            _logger.LogWarning(ex, "Camera connection attempt #{AttemptNumber} failed: {Error}", _connectionAttempts, ex.Message);
            _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},CONNECTION_FAILED,attempt:{_connectionAttempts},error:{ex.Message}");
            
            await SendCameraDisconnectedUpdate();
        }
    }

    private async Task HandleCameraDisconnection()
    {
        _logger.LogInformation("Handling camera disconnection - cleaning up resources");
        _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},DISCONNECTION_HANDLING_STARTED,state:{_connectionState}");

        _connectionState = CameraConnectionState.Disconnected;
        
        // Stop video recording gracefully
        await StopVideoRecording();
        
        // Send disconnected status
        await SendCameraDisconnectedUpdate();
        
        _logger.LogInformation("Camera disconnection handling completed");
        _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},DISCONNECTION_HANDLING_COMPLETED");
    }

    private async Task CleanupCameraResources()
    {
        _logger.LogInformation("Cleaning up camera resources on shutdown");
        
        await StopVideoRecording();
        await FlushFramesToFile();
        await StopMkvProcess();
        await _dataFileWriter.StopAsync(CancellationToken.None);
        
        _logger.LogInformation("Camera resource cleanup completed");
    }

    private async Task StartVideoRecording(CancellationToken stoppingToken)
    {
        if (_connectionState != CameraConnectionState.Connected)
        {
            _logger.LogWarning("Cannot start recording - camera not in connected state (current: {State})", _connectionState);
            return;
        }

        try
        {
            _logger.LogInformation("Starting video recording for connected camera");
            _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_START_ATTEMPT,device:{_devicePath}");
            
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
            
            // Update state to recording
            _connectionState = CameraConnectionState.Recording;
            
            // Send initial connected status to frontend
            await SendCameraConnectedStatus();
            
            // Log recording start to batched file writer
            var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_STARTED,{_devicePath},{CAMERA_WIDTH}x{CAMERA_HEIGHT},capture:{CAMERA_CAPTURE_RATE}fps,multiplier:{CAMERA_FRAME_MULTIPLIER}";
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
            _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_START_FAILED,error:{ex.Message}");
            _connectionState = CameraConnectionState.Connected; // Reset to connected state
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
        DateTime lastFrontendFrameTime = DateTime.UtcNow;
        
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
                        // Buffer frame for writing to current video file
                        var frameData = frameBuffer.ToArray();
                        _frameBuffer.Add(frameData);
                        
                        // Write buffered frames every 10 frames to reduce I/O
                        if (_frameBuffer.Count >= 10)
                        {
                            await FlushFramesToFile();
                        }
                        framesKeptInSegment++;
                        
                        // Debug log every 30 frames to see if data is flowing
                        if (frameCount % 30 == 0)
                        {
                            _logger.LogDebug("Processed {FrameCount} total frames, kept {KeptCount}, current frame size: {FrameSize} bytes", 
                                frameCount, framesKeptInSegment, frameData.Length);
                        }
                        
                        // Send frame to frontend every 1 second
                        var timeSinceLastFrontendFrame = DateTime.UtcNow - lastFrontendFrameTime;
                        if (timeSinceLastFrontendFrame.TotalSeconds >= 1)
                        {
                            _ = Task.Run(() => SendFrameToFrontend(frameData));
                            lastFrontendFrameTime = DateTime.UtcNow;
                        }
                        
                        // Check if we need to start a new video segment
                        var timeSinceLastSegment = DateTime.UtcNow - lastSegmentTime;
                        if (timeSinceLastSegment.TotalSeconds >= VIDEO_SEGMENT_SECONDS)
                        {
                            // Flush remaining frames, complete current segment and start new one
                            await FlushFramesToFile();
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
        
        // Flush any remaining frames and complete the final segment
        await FlushFramesToFile();
        await CompleteCurrentSegment(framesKeptInSegment);
    }
    
    private async Task StartNewVideoSegment(CancellationToken stoppingToken)
    {
        _currentSegmentStartTime = DateTime.UtcNow;
        
        // Wait for shared session to be available from the main data file writer
        while (DataFileWriter.SharedSessionPath == null && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Waiting for shared session path to be available...");
            await Task.Delay(100, stoppingToken);
        }
        
        if (DataFileWriter.SharedSessionPath == null)
        {
            _logger.LogError("No shared session path available for video segment");
            return;
        }
        
        // Create new MKV file path in shared session
        var segmentFileName = $"Camera_{_currentSegmentNumber:D3}.mkv";
        _currentVideoFilePath = Path.Combine(DataFileWriter.SharedSessionPath, segmentFileName);
        
        // Start FFmpeg process to create MKV file from MJPEG stream
        await StartMkvProcess();
        _currentSegmentStartTime = DateTime.UtcNow;
        
        var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},SEGMENT_STARTED,segment:{_currentSegmentNumber},file:{segmentFileName}";
        _dataFileWriter.WriteData(logEntry);
        
        _logger.LogInformation("Started video segment {SegmentNumber}: {FileName} at {FilePath}", 
            _currentSegmentNumber, segmentFileName, _currentVideoFilePath);
    }
    
    private async Task CompleteCurrentSegment(int framesKept)
    {
        // Stop the current MKV process
        await StopMkvProcess();
        
        var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},SEGMENT_COMPLETED,segment:{_currentSegmentNumber},frames_kept:{framesKept}";
        _dataFileWriter.WriteData(logEntry);
        
        _logger.LogInformation("Completed video segment {SegmentNumber} with {FramesKept} frames at {FilePath}", 
            _currentSegmentNumber, framesKept, _currentVideoFilePath);
        
        _currentSegmentNumber++;
        _currentVideoFilePath = null;
    }
    
    private async Task FlushFramesToFile()
    {
        if (_frameBuffer.Count == 0 || _currentMkvProcess == null)
            return;
            
        try
        {
            // Write all buffered frames to FFmpeg stdin for MKV creation
            var stdin = _currentMkvProcess.StandardInput.BaseStream;
            foreach (var frameData in _frameBuffer)
            {
                await stdin.WriteAsync(frameData);
            }
            await stdin.FlushAsync();
            
            _logger.LogDebug("Flushed {FrameCount} frames to MKV process", _frameBuffer.Count);
            _frameBuffer.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing frames to MKV process");
        }
    }
    
    private async Task StartMkvProcess()
    {
        if (string.IsNullOrEmpty(_currentVideoFilePath))
        {
            _logger.LogWarning("Cannot start MKV process - no current video file path set");
            return;
        }
            
        try
        {
            _logger.LogInformation("Starting MKV creation process for {FilePath}", _currentVideoFilePath);
            _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},MKV_PROCESS_START_ATTEMPT,file:{Path.GetFileName(_currentVideoFilePath)}");
            
            var processInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel warning " +
                           $"-f mjpeg -framerate {CAMERA_CAPTURE_RATE / CAMERA_FRAME_MULTIPLIER} -i - " +  // Read MJPEG from stdin
                           "-c copy -f matroska " +  // Copy MJPEG into MKV container
                           $"\"{_currentVideoFilePath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            _currentMkvProcess = Process.Start(processInfo);
            
            if (_currentMkvProcess == null)
            {
                throw new InvalidOperationException("Failed to start MKV creation process");
            }
            
            _logger.LogInformation("Started MKV creation process (PID: {ProcessId}) for {FilePath}", _currentMkvProcess.Id, _currentVideoFilePath);
            _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},MKV_PROCESS_STARTED,pid:{_currentMkvProcess.Id},file:{Path.GetFileName(_currentVideoFilePath)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start MKV creation process for {FilePath}", _currentVideoFilePath);
            _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},MKV_PROCESS_START_FAILED,file:{Path.GetFileName(_currentVideoFilePath ?? "unknown")},error:{ex.Message}");
            throw;
        }
    }
    
    private async Task StopMkvProcess()
    {
        if (_currentMkvProcess != null && !_currentMkvProcess.HasExited)
        {
            try
            {
                _logger.LogInformation("Stopping MKV creation process (PID: {ProcessId}) for {FilePath}", _currentMkvProcess.Id, _currentVideoFilePath);
                _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},MKV_PROCESS_STOP_ATTEMPT,pid:{_currentMkvProcess.Id},file:{Path.GetFileName(_currentVideoFilePath ?? "unknown")}");
                
                // Close stdin to signal end of stream
                _currentMkvProcess.StandardInput.Close();
                
                // Wait for FFmpeg to finish processing
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _currentMkvProcess.WaitForExitAsync(cts.Token);
                
                _logger.LogInformation("MKV creation process completed successfully (exit code: {ExitCode}) for {FilePath}", _currentMkvProcess.ExitCode, _currentVideoFilePath);
                _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},MKV_PROCESS_COMPLETED,exit_code:{_currentMkvProcess.ExitCode},file:{Path.GetFileName(_currentVideoFilePath ?? "unknown")}");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("MKV creation process did not complete within timeout - forcing termination");
                _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},MKV_PROCESS_TIMEOUT,forced_kill,file:{Path.GetFileName(_currentVideoFilePath ?? "unknown")}");
                try
                {
                    _currentMkvProcess.Kill();
                }
                catch (Exception killEx)
                {
                    _logger.LogError(killEx, "Failed to force kill MKV creation process");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping MKV creation process");
                _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},MKV_PROCESS_STOP_ERROR,error:{ex.Message},file:{Path.GetFileName(_currentVideoFilePath ?? "unknown")}");
                try
                {
                    _currentMkvProcess.Kill();
                }
                catch (Exception killEx)
                {
                    _logger.LogError(killEx, "Failed to kill MKV creation process after error");
                }
            }
            finally
            {
                _currentMkvProcess?.Dispose();
                _currentMkvProcess = null;
            }
        }
        else if (_currentMkvProcess != null)
        {
            _logger.LogDebug("MKV creation process already exited with code {ExitCode}", _currentMkvProcess.ExitCode);
            _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},MKV_PROCESS_ALREADY_STOPPED,exit_code:{_currentMkvProcess.ExitCode}");
        }
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
                _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_STOP_ATTEMPT,process_id:{_ffmpegProcess.Id}");
                
                // Kill the FFmpeg process
                _ffmpegProcess.Kill(entireProcessTree: false);
                
                // Wait for process to exit with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _ffmpegProcess.WaitForExitAsync(cts.Token);
                
                _logger.LogInformation("MJPEG stream capture stopped successfully");
                
                // Log recording stop to batched file writer
                var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_STOPPED,{_devicePath},exit_code:{_ffmpegProcess.ExitCode}";
                _dataFileWriter.WriteData(logEntry);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("FFmpeg process did not exit within timeout - forcing termination");
                _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_STOP_TIMEOUT,forced_kill");
                try
                {
                    _ffmpegProcess.Kill(entireProcessTree: true);
                }
                catch (Exception killEx)
                {
                    _logger.LogError(killEx, "Failed to force kill FFmpeg process");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping MJPEG stream capture");
                _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_STOP_ERROR,error:{ex.Message}");
            }
            finally
            {
                _ffmpegProcess?.Dispose();
                _ffmpegProcess = null;
                
                // Update connection state if we were recording
                if (_connectionState == CameraConnectionState.Recording)
                {
                    _connectionState = File.Exists(_devicePath) ? CameraConnectionState.Connected : CameraConnectionState.Disconnected;
                    _logger.LogDebug("Updated connection state to {State} after stopping recording", _connectionState);
                }
            }
        }
        else if (_ffmpegProcess != null)
        {
            _logger.LogDebug("FFmpeg process already exited with code {ExitCode}", _ffmpegProcess.ExitCode);
            _dataFileWriter.WriteData($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff},RECORDING_ALREADY_STOPPED,exit_code:{_ffmpegProcess.ExitCode}");
        }
    }
    
    private async Task SendFrameToFrontend(byte[] frameData)
    {
        try
        {
            var base64Image = Convert.ToBase64String(frameData);
            
            var cameraUpdate = new CameraUpdate
            {
                Timestamp = DateTime.UtcNow,
                ImageBase64 = base64Image,
                ImageSizeBytes = frameData.Length,
                ImageWidth = CAMERA_WIDTH,
                ImageHeight = CAMERA_HEIGHT,
                Format = "JPEG",
                CaptureTimeMs = 0, // We don't track individual capture times
                EncodingTimeMs = 0, // No encoding since we're using raw MJPEG frames
                IsConnected = true
            };

            await _hubContext.Clients.All.SendAsync("CameraUpdate", cameraUpdate);
            _logger.LogDebug("Sent frame to frontend: {Size} bytes", frameData.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send frame to frontend");
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

    private async Task SendCameraConnectedStatus()
    {
        try
        {
            var statusUpdate = new CameraUpdate
            {
                Timestamp = DateTime.UtcNow,
                ImageBase64 = string.Empty,
                ImageSizeBytes = 0,
                ImageWidth = CAMERA_WIDTH,
                ImageHeight = CAMERA_HEIGHT,
                Format = "MKV_RECORDING",
                CaptureTimeMs = 0,
                EncodingTimeMs = 0,
                IsConnected = true
            };

            await _hubContext.Clients.All.SendAsync("CameraUpdate", statusUpdate);
            _logger.LogDebug("Camera connected status sent via SignalR");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send camera status via SignalR");
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
        
        // Flush any remaining frames and stop MKV process
        await FlushFramesToFile();
        await StopMkvProcess();
        
        await _dataFileWriter.StopAsync(cancellationToken);
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