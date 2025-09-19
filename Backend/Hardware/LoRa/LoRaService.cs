using System.IO.Ports;

namespace Backend.Hardware.LoRa;

public class LoRaService : BackgroundService
{
    private const string LORA_PORT = "/dev/ttyUSB0";
    private const int LORA_BAUD_RATE = 9600;

    private readonly ILogger<LoRaService> _logger;
    private SerialPort? _loraPort;
    private long _loraBytesSent = 0;
    private long _loraBytesReceived = 0;
    private long _totalLoRaBytesSent = 0;
    private long _totalLoRaBytesReceived = 0;
    private DateTime _lastRateUpdate = DateTime.UtcNow;

    public event EventHandler<byte[]>? DataReceived;

    public LoRaService(ILogger<LoRaService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LoRa Service started - will connect on first operation");

        try
        {
            // Keep service running and monitor connection
            while (!stoppingToken.IsCancellationRequested)
            {
                await UpdateDataRatesAsync(stoppingToken);
                await Task.Delay(1000, stoppingToken); // Check every second
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error in LoRa service");
        }

        _logger.LogInformation("LoRa Service ExecuteAsync completed");
    }


    private async Task InitializeLoRaPort()
    {
        try
        {
            _logger.LogDebug("📡 LoRa: Creating SerialPort for {Port} at {BaudRate} baud", LORA_PORT, LORA_BAUD_RATE);
            _loraPort = new SerialPort(LORA_PORT, LORA_BAUD_RATE, Parity.None, 8, StopBits.One);

            // Set up data received event handler
            _loraPort.DataReceived += OnSerialDataReceived;

            _logger.LogDebug("📡 LoRa: Opening port connection...");
            _loraPort.Open();

            _logger.LogInformation("📡 LoRa: Successfully connected to {Port}", LORA_PORT);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 LoRa: Failed to open port {Port} - will retry on next operation. Check if LoRa device is connected.", LORA_PORT);
            _loraPort = null;
        }
    }

    private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (_loraPort?.IsOpen == true)
            {
                var bytesToRead = _loraPort.BytesToRead;
                if (bytesToRead > 0)
                {
                    var buffer = new byte[bytesToRead];
                    var bytesRead = _loraPort.Read(buffer, 0, bytesToRead);

                    if (bytesRead > 0)
                    {
                        var data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);

                        _loraBytesReceived += bytesRead;
                        _totalLoRaBytesReceived += bytesRead;

                        _logger.LogDebug("📡 LoRa: Received {Length} bytes - Session Total: {TotalBytes} bytes", bytesRead, _totalLoRaBytesReceived);

                        // Notify subscribers
                        DataReceived?.Invoke(this, data);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 LoRa: Error reading received data");
        }
    }

    public async Task SendData(byte[] data)
    {
        _logger.LogDebug("📡 LoRa: SendData called with {Length} bytes", data.Length);

        // Check if LoRa port exists
        if (!File.Exists(LORA_PORT))
        {
            _logger.LogDebug("📡 LoRa: Port {Port} does not exist - no device connected", LORA_PORT);
            return;
        }

        _logger.LogDebug("📡 LoRa: Port {Port} exists, checking connection", LORA_PORT);

        // Initialize connection if needed
        if (_loraPort == null || !_loraPort.IsOpen)
        {
            _logger.LogInformation("📡 LoRa: Initializing connection to {Port}", LORA_PORT);
            await InitializeLoRaPort();
            if (_loraPort == null || !_loraPort.IsOpen)
            {
                _logger.LogWarning("📡 LoRa: Failed to establish connection");
                return;
            }
        }

        try
        {
            // Write data to LoRa
            _loraPort.Write(data, 0, data.Length);
            _loraBytesSent += data.Length;
            _totalLoRaBytesSent += data.Length;

            _logger.LogDebug("📡 LoRa: Successfully sent {Length} bytes - Session Total: {TotalBytes} bytes", data.Length, _totalLoRaBytesSent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 LoRa: Failed to send data. Will attempt to reconnect.");

            // Try to reconnect
            await TryReconnectLoRaPort();
        }
    }

    private async Task TryReconnectLoRaPort()
    {
        try
        {
            _logger.LogInformation("📡 LoRa: Attempting to reconnect...");
            if (_loraPort != null)
            {
                _loraPort.DataReceived -= OnSerialDataReceived;
                _loraPort.Close();
                _loraPort.Dispose();
            }
            _logger.LogDebug("📡 LoRa: Waiting 2 seconds before reconnection attempt");
            await Task.Delay(2000); // Wait before reconnecting
            await InitializeLoRaPort();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 LoRa: Reconnection attempt failed");
        }
    }

    private async Task UpdateDataRatesAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("UpdateDataRatesAsync called (cancellation requested: {Cancelled})", stoppingToken.IsCancellationRequested);

        // Immediately exit if cancellation is requested
        stoppingToken.ThrowIfCancellationRequested();

        var now = DateTime.UtcNow;
        var timeDelta = (now - _lastRateUpdate).TotalSeconds;
        _logger.LogDebug("UpdateDataRatesAsync timeDelta: {TimeDelta}s", timeDelta);

        // Update rates every 5 seconds
        if (timeDelta >= 5.0)
        {
            _logger.LogDebug("UpdateDataRatesAsync processing rates (cancellation: {Cancelled})", stoppingToken.IsCancellationRequested);
            // Check again before any processing
            stoppingToken.ThrowIfCancellationRequested();

            // Calculate LoRa rates in kbps (kilobits per second)
            CurrentSendRate = (_loraBytesSent * 8.0) / (timeDelta * 1000.0);
            CurrentReceiveRate = (_loraBytesReceived * 8.0) / (timeDelta * 1000.0);
            var totalSentBytesInPeriod = _loraBytesSent;
            var totalReceivedBytesInPeriod = _loraBytesReceived;

            // Reset counters
            _loraBytesSent = 0;
            _loraBytesReceived = 0;
            _lastRateUpdate = now;

            // Final check before logging
            stoppingToken.ThrowIfCancellationRequested();

            if (CurrentSendRate > 0 || CurrentReceiveRate > 0)
            {
                _logger.LogInformation("📊 LoRa Rates - Send: {SendRate:F1} kbps ({SentBytes} bytes), Receive: {ReceiveRate:F1} kbps ({ReceivedBytes} bytes) in {Period:F1}s - Session Totals: {SessionSent} sent, {SessionReceived} received [Cancellation: {Cancelled}]",
                    CurrentSendRate, totalSentBytesInPeriod, CurrentReceiveRate, totalReceivedBytesInPeriod, timeDelta, _totalLoRaBytesSent, _totalLoRaBytesReceived, stoppingToken.IsCancellationRequested);
            }
        }
        _logger.LogDebug("UpdateDataRatesAsync completed");
    }

    public bool IsConnected => _loraPort?.IsOpen == true;

    public double CurrentSendRate { get; private set; } = 0.0;
    public double CurrentReceiveRate { get; private set; } = 0.0;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping LoRa Service");

        await base.StopAsync(cancellationToken);

        // Clean up serial port reference
        if (_loraPort != null)
        {
            _loraPort.DataReceived -= OnSerialDataReceived;
            _loraPort = null;
        }
    }

    public override void Dispose()
    {
        // Port already cleaned up in StopAsync - skip base.Dispose() to avoid delay
        _loraPort = null;
    }
}