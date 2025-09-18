using System.IO.Ports;
using Backend.Configuration;

namespace Backend.Hardware.Bluetooth;

public class BluetoothStreamingService : BackgroundService
{
    private const string BLUETOOTH_PORT = "/dev/rfcomm0";
    private const int BLUETOOTH_BAUD_RATE = 9600;
    
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
        _logger.LogInformation("Bluetooth Streaming Service started - will connect on first data send");

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
            _logger.LogDebug("📡 Bluetooth: Creating SerialPort for {Port} at {BaudRate} baud", BLUETOOTH_PORT, BLUETOOTH_BAUD_RATE);
            _bluetoothPort = new SerialPort(BLUETOOTH_PORT, BLUETOOTH_BAUD_RATE, Parity.None, 8, StopBits.One);
            
            _logger.LogDebug("📡 Bluetooth: Opening port connection...");
            _bluetoothPort.Open();
            
            _logger.LogInformation("📡 Bluetooth: Successfully connected to {Port}", BLUETOOTH_PORT);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 Bluetooth: Failed to open port {Port} - will retry on next data send. Check if device is paired and rfcomm port is bound.", BLUETOOTH_PORT);
            _bluetoothPort = null;
        }
    }

    public async Task SendData(byte[] data)
    {
        _logger.LogDebug("📡 Bluetooth: SendData called with {Length} bytes", data.Length);

        // Check if Bluetooth streaming is enabled
        if (!SystemConfiguration.BluetoothStreamingEnabled)
        {
            _logger.LogDebug("📡 Bluetooth: Streaming disabled in configuration");
            return;
        }

        // Check if Bluetooth port exists
        if (!File.Exists(BLUETOOTH_PORT))
        {
            _logger.LogDebug("📡 Bluetooth: Port {Port} does not exist - no device connected", BLUETOOTH_PORT);
            return;
        }

        _logger.LogDebug("📡 Bluetooth: Port {Port} exists, checking connection", BLUETOOTH_PORT);

        // Initialize connection if needed
        if (_bluetoothPort == null || !_bluetoothPort.IsOpen)
        {
            _logger.LogInformation("📡 Bluetooth: Initializing connection to {Port}", BLUETOOTH_PORT);
            await InitializeBluetoothPort();
            if (_bluetoothPort == null || !_bluetoothPort.IsOpen)
            {
                _logger.LogWarning("📡 Bluetooth: Failed to establish connection");
                return;
            }
        }

        try
        {
            // Write data to Bluetooth
            _bluetoothPort.Write(data, 0, data.Length);
            _bluetoothBytesSent += data.Length;
            _totalBluetoothBytesSent += data.Length;

            _logger.LogDebug("📡 Bluetooth: Successfully sent {Length} bytes - Session Total: {TotalBytes} bytes", data.Length, _totalBluetoothBytesSent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 Bluetooth: Failed to send data. Will attempt to reconnect.");
            
            // Try to reconnect
            await TryReconnectBluetoothPort();
        }
    }

    private async Task TryReconnectBluetoothPort()
    {
        try
        {
            _logger.LogInformation("📡 Bluetooth: Attempting to reconnect...");
            _bluetoothPort?.Close();
            _bluetoothPort?.Dispose();
            _logger.LogDebug("📡 Bluetooth: Waiting 2 seconds before reconnection attempt");
            await Task.Delay(2000); // Wait before reconnecting
            await InitializeBluetoothPort();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 Bluetooth: Reconnection attempt failed");
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