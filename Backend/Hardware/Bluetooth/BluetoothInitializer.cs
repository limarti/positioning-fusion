using System.Diagnostics;
using Backend.Configuration;

namespace Backend.Hardware.Bluetooth;

public class BluetoothInitializer : IDisposable
{
    private readonly ILogger<BluetoothInitializer> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly GeoConfigurationManager _configurationManager;
    private Process? _bluetoothCtlProcess;
    private StreamWriter? _bluetoothCtlStdin;
    private Process? _rfcommProcess;
    private SppProfileManager? _sppProfileManager;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _outputMonitorTask;
    private Task? _rfcommMonitorTask;
    private bool _disposed = false;
    private readonly object _processLock = new object();

    private const string RFCOMM_DEVICE = "/dev/rfcomm0";
    private const int RFCOMM_CHANNEL = 1;

    public bool IsInitialized { get; private set; } = false;

    public BluetoothInitializer(ILogger<BluetoothInitializer> logger, ILoggerFactory loggerFactory, GeoConfigurationManager configurationManager)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _configurationManager = configurationManager;
    }

    public async Task<bool> InitializeAsync()
    {
        _logger.LogInformation("Initializing Bluetooth adapter");

        try
        {
            // Start the interactive bluetoothctl process
            if (!await StartBluetoothCtlProcessAsync())
            {
                _logger.LogError("Failed to start bluetoothctl process");
                return false;
            }

            // Send initialization commands
            await SendCommandAsync("power on");
            await Task.Delay(500);

            // Register SPP profile via D-Bus BEFORE starting rfcomm
            _sppProfileManager = new SppProfileManager(_loggerFactory.CreateLogger<SppProfileManager>());
            if (!await _sppProfileManager.RegisterProfileAsync())
            {
                _logger.LogError("Failed to register SPP profile");
                return false;
            }

            // Start rfcomm listener (now that SPP profile is registered)
            if (!await StartRfcommListenerAsync())
            {
                _logger.LogError("Failed to start rfcomm listener");
                return false;
            }

            // Set device name after powering on
            var deviceName = _configurationManager.BluetoothName;
            _logger.LogInformation("Setting Bluetooth device name to: {Name}", deviceName);
            await SendCommandAsync($"system-alias {deviceName}");
            await Task.Delay(500);

            await SendCommandAsync("agent on");
            await Task.Delay(200);

            await SendCommandAsync("default-agent");
            await Task.Delay(200);

            await SendCommandAsync("discoverable on");
            await Task.Delay(200);

            await SendCommandAsync("pairable on");
            await Task.Delay(200);

            _logger.LogInformation("Bluetooth adapter initialized successfully - Name: {Name}, Discoverable: Yes, Pairable: Yes",
                _configurationManager.BluetoothName);

            IsInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Bluetooth adapter");
            IsInitialized = false;
            return false;
        }
    }

    private Task<bool> StartBluetoothCtlProcessAsync()
    {
        try
        {
            lock (_processLock)
            {
                if (_bluetoothCtlProcess != null)
                {
                    _logger.LogDebug("bluetoothctl process already running");
                    return Task.FromResult(true);
                }

                _cancellationTokenSource = new CancellationTokenSource();

                _bluetoothCtlProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "bluetoothctl",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                _bluetoothCtlProcess.Start();
                _bluetoothCtlStdin = _bluetoothCtlProcess.StandardInput;

                _logger.LogInformation("Started bluetoothctl interactive process (PID: {ProcessId})", _bluetoothCtlProcess.Id);
            }

            // Start monitoring output for pairing requests
            _outputMonitorTask = Task.Run(() => MonitorOutputAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start bluetoothctl process");
            return Task.FromResult(false);
        }
    }

    private async Task MonitorOutputAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Started bluetoothctl output monitor");

        try
        {
            var stdout = _bluetoothCtlProcess?.StandardOutput;
            if (stdout == null)
            {
                _logger.LogError("bluetoothctl stdout is null");
                return;
            }

            while (!cancellationToken.IsCancellationRequested && !stdout.EndOfStream)
            {
                var line = await stdout.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(line))
                    continue;

                _logger.LogDebug("bluetoothctl: {Line}", line);

                // Check for pairing requests and accept them
                if (IsPairingRequest(line))
                {
                    _logger.LogInformation("Detected pairing request: {Line}", line);
                    await AcceptPairingAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("bluetoothctl output monitor cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in bluetoothctl output monitor");
        }

        _logger.LogInformation("bluetoothctl output monitor stopped");
    }

    private Task<bool> StartRfcommListenerAsync()
    {
        try
        {
            lock (_processLock)
            {
                if (_rfcommProcess != null)
                {
                    _logger.LogDebug("rfcomm listener already running");
                    return Task.FromResult(true);
                }

                _rfcommProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "rfcomm",
                        Arguments = $"listen {RFCOMM_DEVICE} {RFCOMM_CHANNEL}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                _rfcommProcess.Start();

                _logger.LogInformation("Started rfcomm listener on {Device} channel {Channel} (PID: {ProcessId})",
                    RFCOMM_DEVICE, RFCOMM_CHANNEL, _rfcommProcess.Id);

                // Start monitoring output for connection events (fire and forget)
                _rfcommMonitorTask = Task.Run(() => MonitorRfcommOutputAsync(_cancellationTokenSource!.Token), _cancellationTokenSource!.Token);
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start rfcomm listener");
            return Task.FromResult(false);
        }
    }

    private async Task MonitorRfcommOutputAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Started rfcomm output monitor - watching for connection attempts");

        try
        {
            var stdout = _rfcommProcess?.StandardOutput;
            var stderr = _rfcommProcess?.StandardError;

            if (stdout == null || stderr == null)
            {
                _logger.LogError("rfcomm stdout/stderr is null");
                return;
            }

            // Monitor both stdout and stderr
            var stdoutTask = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested && !stdout.EndOfStream)
                {
                    var line = await stdout.ReadLineAsync(cancellationToken);
                    if (!string.IsNullOrEmpty(line))
                    {
                        _logger.LogInformation("📱 rfcomm stdout: {Line}", line);

                        // Log connection events with more detail
                        if (line.Contains("connect", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("✅ Bluetooth client CONNECTED on {Device} - port should now be available", RFCOMM_DEVICE);

                            // Check if port was created
                            if (File.Exists(RFCOMM_DEVICE))
                            {
                                _logger.LogInformation("✅ Verified {Device} exists and is ready for use", RFCOMM_DEVICE);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ {Device} does not exist yet after connection", RFCOMM_DEVICE);
                            }
                        }
                        else if (line.Contains("disconnect", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("❌ Bluetooth client DISCONNECTED from {Device}", RFCOMM_DEVICE);
                        }
                        else if (line.Contains("waiting", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("⏳ rfcomm is waiting for incoming connections on channel {Channel}", RFCOMM_CHANNEL);
                        }
                    }
                }
            }, cancellationToken);

            var stderrTask = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested && !stderr.EndOfStream)
                {
                    var line = await stderr.ReadLineAsync(cancellationToken);
                    if (!string.IsNullOrEmpty(line))
                    {
                        _logger.LogError("❌ rfcomm ERROR: {Line}", line);
                    }
                }
            }, cancellationToken);

            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("rfcomm output monitor cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in rfcomm output monitor");
        }

        _logger.LogInformation("rfcomm output monitor stopped");
    }

    private bool IsPairingRequest(string line)
    {
        // Check for common pairing prompts from bluetoothctl
        var pairingIndicators = new[]
        {
            "Confirm passkey",
            "Request confirmation",
            "Authorize service",
            "Request authorization",
            "[agent] Confirm",
            "[agent] Authorize",
            "Pairing successful"
        };

        return pairingIndicators.Any(indicator => line.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private async Task AcceptPairingAsync()
    {
        try
        {
            _logger.LogInformation("Accepting pairing request");
            await SendCommandAsync("yes");
            _logger.LogInformation("Pairing request accepted");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to accept pairing request");
        }
    }

    private async Task SendCommandAsync(string command)
    {
        lock (_processLock)
        {
            if (_bluetoothCtlStdin == null || _bluetoothCtlProcess?.HasExited == true)
            {
                _logger.LogWarning("Cannot send command '{Command}' - bluetoothctl process not available", command);
                return;
            }
        }

        try
        {
            await _bluetoothCtlStdin!.WriteLineAsync(command);
            await _bluetoothCtlStdin.FlushAsync();
            _logger.LogDebug("Sent bluetoothctl command: {Command}", command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send bluetoothctl command: {Command}", command);
        }
    }

    private async Task<(bool Success, string Output, string Error)> ExecuteBluetoothCtlCommandAsync(string command)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "bluetoothctl",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _logger.LogDebug("Executing: bluetoothctl {Command}", command);

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;

            var success = process.ExitCode == 0;

            if (!success)
            {
                _logger.LogDebug("bluetoothctl command failed: {Command}, Exit Code: {ExitCode}, Error: {Error}",
                    command, process.ExitCode, error);
            }

            return (success, output, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception executing bluetoothctl command: {Command}", command);
            return (false, string.Empty, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _logger.LogInformation("Disposing BluetoothInitializer");

        lock (_processLock)
        {
            // Cancel monitoring tasks
            _cancellationTokenSource?.Cancel();

            // Close stdin to signal bluetoothctl to exit gracefully
            _bluetoothCtlStdin?.Close();
            _bluetoothCtlStdin?.Dispose();
            _bluetoothCtlStdin = null;

            // Wait for bluetoothctl process to exit
            if (_bluetoothCtlProcess != null && !_bluetoothCtlProcess.HasExited)
            {
                if (!_bluetoothCtlProcess.WaitForExit(2000))
                {
                    _logger.LogWarning("bluetoothctl process did not exit gracefully, killing it");
                    _bluetoothCtlProcess.Kill();
                }
            }

            _bluetoothCtlProcess?.Dispose();
            _bluetoothCtlProcess = null;

            // Stop rfcomm listener
            if (_rfcommProcess != null && !_rfcommProcess.HasExited)
            {
                _logger.LogInformation("Stopping rfcomm listener");
                try
                {
                    _rfcommProcess.Kill();
                    if (!_rfcommProcess.WaitForExit(2000))
                    {
                        _logger.LogWarning("rfcomm process did not exit, forcing termination");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping rfcomm process");
                }
            }

            _rfcommProcess?.Dispose();
            _rfcommProcess = null;

            // Unregister SPP profile
            _sppProfileManager?.Dispose();
            _sppProfileManager = null;
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _logger.LogInformation("BluetoothInitializer disposed");
    }
}
