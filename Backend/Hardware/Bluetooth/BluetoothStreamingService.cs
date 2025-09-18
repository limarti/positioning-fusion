using System.IO.Ports;

namespace Backend.Hardware.Bluetooth;

public class BluetoothStreamingService : BackgroundService
{
    private readonly ILogger<BluetoothStreamingService> _logger;
    private SerialPort? _bluetoothPort;
    private long _bluetoothBytesSent = 0;
    private long _totalBluetoothBytesSent = 0;
    private DateTime _lastRateUpdate = DateTime.UtcNow;

    public BluetoothStreamingService(ILogger<BluetoothStreamingService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Bluetooth Streaming Service started");

        await InitializeBluetoothPort();

        // Keep service running and monitor connection
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateDataRatesAsync(stoppingToken);
                await Task.Delay(1000, stoppingToken); // Check every second
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Bluetooth streaming service");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task InitializeBluetoothPort()
    {
        try
        {
            _bluetoothPort = new SerialPort("/dev/rfcomm0", 9600, Parity.None, 8, StopBits.One);
            _bluetoothPort.Open();
            _logger.LogInformation("Bluetooth streaming enabled on /dev/rfcomm0");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open Bluetooth port /dev/rfcomm0 - will retry connection attempts");
            _bluetoothPort = null;
        }
    }

    public async Task SendData(byte[] data)
    {
        if (_bluetoothPort == null || !_bluetoothPort.IsOpen)
            return;

        try
        {
            // Write data to Bluetooth
            _bluetoothPort.Write(data, 0, data.Length);
            _bluetoothBytesSent += data.Length;
            _totalBluetoothBytesSent += data.Length;

            //_logger.LogInformation("📡 Bluetooth: Sent {Length} bytes - Session Total: {TotalBytes} bytes", data.Length, _totalBluetoothBytesSent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send data to Bluetooth. Will attempt to reconnect.");
            
            // Try to reconnect
            await TryReconnectBluetoothPort();
        }
    }

    private async Task TryReconnectBluetoothPort()
    {
        try
        {
            _bluetoothPort?.Close();
            _bluetoothPort?.Dispose();
            await Task.Delay(2000); // Wait before reconnecting
            await InitializeBluetoothPort();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bluetooth reconnection failed");
        }
    }

    private async Task UpdateDataRatesAsync(CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;
        var timeDelta = (now - _lastRateUpdate).TotalSeconds;

        // Update rates every 5 seconds
        if (timeDelta >= 5.0)
        {
            // Calculate Bluetooth rate in kbps (kilobits per second)
            var bluetoothRate = (_bluetoothBytesSent * 8.0) / (timeDelta * 1000.0);
            var totalBytesInPeriod = _bluetoothBytesSent;

            // Reset counters
            _bluetoothBytesSent = 0;
            _lastRateUpdate = now;

            if (bluetoothRate > 0)
            {
                //_logger.LogInformation("📊 Bluetooth Rate: {BluetoothRate:F1} kbps ({PeriodBytes} bytes in {Period:F1}s) - Session Total: {SessionTotal} bytes",
                //    bluetoothRate, totalBytesInPeriod, timeDelta, _totalBluetoothBytesSent);
            }
        }
    }

    public bool IsConnected => _bluetoothPort?.IsOpen == true;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Bluetooth Streaming Service");
        
        // Close Bluetooth port if open
        if (_bluetoothPort?.IsOpen == true)
        {
            try
            {
                _bluetoothPort.Close();
                _logger.LogInformation("Bluetooth port closed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing Bluetooth port");
            }
        }
        
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _logger.LogInformation("Bluetooth Streaming Service disposing");
        
        // Dispose Bluetooth port
        _bluetoothPort?.Dispose();
        
        base.Dispose();
    }
}