using System.Diagnostics;

namespace Backend.Hardware.Camera;

public class CameraInitializer
{
    private readonly ILogger<CameraInitializer> _logger;
    private bool _cameraAvailable = false;
    private const string DefaultCameraDevice = "/dev/video0";

    public CameraInitializer(ILogger<CameraInitializer> logger)
    {
        _logger = logger;
    }

    public async Task<bool> InitializeAsync()
    {
        _logger.LogInformation("Starting camera initialization for device: {Device}", DefaultCameraDevice);
        
        try
        {
            // Check if camera device exists
            if (!File.Exists(DefaultCameraDevice))
            {
                _logger.LogError("Camera device not found at: {Device}", DefaultCameraDevice);
                return false;
            }

            _logger.LogInformation("Camera device found at: {Device}", DefaultCameraDevice);

            // Test camera functionality using v4l2-ctl
            var testResult = await TestCameraAsync();
            if (!testResult)
            {
                _logger.LogError("Camera test failed for device: {Device}", DefaultCameraDevice);
                _cameraAvailable = false;
                return false;
            }

            _logger.LogInformation("Camera device initialized successfully");
            _cameraAvailable = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during camera initialization");
            _cameraAvailable = false;
            return false;
        }
    }

    public bool IsCameraAvailable()
    {
        return _cameraAvailable;
    }

    private async Task<bool> TestCameraAsync()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "v4l2-ctl",
                Arguments = $"--device={DefaultCameraDevice} --list-formats",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null) return false;

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
}