using System.Diagnostics;
using Backend.Configuration;

namespace Backend.Hardware.Bluetooth;

public class BluetoothInitializer : IDisposable
{
    private readonly ILogger<BluetoothInitializer> _logger;
    private readonly GeoConfigurationManager _configurationManager;
    private Process? _bluetoothCtlProcess;
    private StreamWriter? _bluetoothCtlStdin;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _outputMonitorTask;
    private bool _disposed = false;
    private readonly object _processLock = new object();

    public bool IsInitialized { get; private set; } = false;

    public BluetoothInitializer(ILogger<BluetoothInitializer> logger, GeoConfigurationManager configurationManager)
    {
        _logger = logger;
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

    private async Task<bool> StartBluetoothCtlProcessAsync()
    {
        try
        {
            lock (_processLock)
            {
                if (_bluetoothCtlProcess != null)
                {
                    _logger.LogDebug("bluetoothctl process already running");
                    return true;
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

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start bluetoothctl process");
            return false;
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
            // Cancel monitoring task
            _cancellationTokenSource?.Cancel();

            // Close stdin to signal bluetoothctl to exit gracefully
            _bluetoothCtlStdin?.Close();
            _bluetoothCtlStdin?.Dispose();
            _bluetoothCtlStdin = null;

            // Wait for process to exit
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
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _logger.LogInformation("BluetoothInitializer disposed");
    }
}
