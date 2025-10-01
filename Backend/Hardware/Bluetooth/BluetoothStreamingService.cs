using System.IO.Ports;
using Backend.Configuration;
using Backend.Hardware.Common;

namespace Backend.Hardware.Bluetooth;

public class BluetoothStreamingService : BackgroundService
{
    private const string BLUETOOTH_PORT = "/dev/rfcomm0";
    private const int BLUETOOTH_BAUD_RATE = 9600;
    
    private readonly ILogger<BluetoothStreamingService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private SerialPortManager? _serialPortManager;
    private long _bluetoothBytesSent = 0;
    private long _totalBluetoothBytesSent = 0;
    private DateTime _lastRateUpdate = DateTime.UtcNow;
    private DateTime _lastBluetoothSend = DateTime.UtcNow;

    public BluetoothStreamingService(ILogger<BluetoothStreamingService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
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
            _logger.LogDebug("📡 Bluetooth: Creating SerialPortManager for {Port} at {BaudRate} baud", BLUETOOTH_PORT, BLUETOOTH_BAUD_RATE);

            _serialPortManager = new SerialPortManager(
                "Bluetooth",
                BLUETOOTH_PORT,
                BLUETOOTH_BAUD_RATE,
                _loggerFactory.CreateLogger<SerialPortManager>(),
                100, // pollingIntervalMs
                1024, // readBufferSize
                10240 // maxBufferSize
            );

            _logger.LogDebug("📡 Bluetooth: Starting SerialPortManager...");
            await _serialPortManager.StartAsync();

            _logger.LogInformation("📡 Bluetooth: Successfully connected to {Port}", BLUETOOTH_PORT);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 Bluetooth: Failed to open port {Port} - will retry on next data send. Check if device is paired and rfcomm port is bound.", BLUETOOTH_PORT);
            _serialPortManager = null;
        }
    }

    public async Task SendData(byte[] data)
    {
        // Check if Bluetooth port exists
        if (!File.Exists(BLUETOOTH_PORT))
        {
            // Only log occasionally to avoid spam
            var now = DateTime.UtcNow;
            if ((now - _lastBluetoothSend).TotalSeconds >= 10)
            {
                _logger.LogDebug("📡 Bluetooth: Port {Port} does not exist - waiting for client connection", BLUETOOTH_PORT);
                _lastBluetoothSend = now;
            }
            return;
        }

        // Log first successful detection of port
        if (_serialPortManager == null)
        {
            _logger.LogInformation("📡 Bluetooth: Port {Port} detected! Attempting to connect...", BLUETOOTH_PORT);
        }

        // Initialize connection if needed
        if (_serialPortManager == null || !_serialPortManager.IsConnected)
        {
            _logger.LogInformation("📡 Bluetooth: Initializing connection to {Port}", BLUETOOTH_PORT);
            await InitializeBluetoothPort();
            if (_serialPortManager == null || !_serialPortManager.IsConnected)
            {
                _logger.LogWarning("📡 Bluetooth: Failed to establish connection");
                return;
            }
        }

        try
        {
            // Write data to Bluetooth
            _serialPortManager.Write(data, 0, data.Length);
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
            if (_serialPortManager != null)
            {
                await _serialPortManager.StopAsync();
                _serialPortManager.Dispose();
                _serialPortManager = null;
            }
            _logger.LogDebug("📡 Bluetooth: Waiting 2 seconds before reconnection attempt");
            await Task.Delay(2000); // Wait before reconnecting
            await InitializeBluetoothPort();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 Bluetooth: Reconnection attempt failed");
        }
    }

    private Task UpdateDataRatesAsync(CancellationToken stoppingToken)
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

        return Task.CompletedTask;
    }

    public bool IsConnected => _serialPortManager?.IsConnected == true;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Bluetooth Streaming Service");
        
        // Stop SerialPortManager if connected
        if (_serialPortManager != null)
        {
            try
            {
                await _serialPortManager.StopAsync();
                _serialPortManager.Dispose();
                _logger.LogInformation("Bluetooth SerialPortManager stopped");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping Bluetooth SerialPortManager");
            }
        }
        
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _logger.LogInformation("Bluetooth Streaming Service disposing");
        
        // SerialPortManager disposal is handled in StopAsync
        
        base.Dispose();
    }
}