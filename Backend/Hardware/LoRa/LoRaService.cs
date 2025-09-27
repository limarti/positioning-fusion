using System.IO.Ports;
using Backend.Configuration;
using Backend.Hardware.Common;

namespace Backend.Hardware.LoRa;

public class LoRaService : BackgroundService
{
    private const string LORA_PORT = "/dev/ttyUSB0";
    private const int LORA_BAUD_RATE = 57600;

    private readonly ILogger<LoRaService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly GeoConfigurationManager _configurationManager;
    private SerialPortManager? _serialPortManager;
    private long _loraBytesSent = 0;
    private long _loraBytesReceived = 0;
    private long _totalLoRaBytesSent = 0;
    private long _totalLoRaBytesReceived = 0;
    private DateTime _lastRateUpdate = DateTime.UtcNow;

    public event EventHandler<byte[]>? DataReceived;

    public LoRaService(ILogger<LoRaService> logger, GeoConfigurationManager configurationManager, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _configurationManager = configurationManager;

        // Initialize SerialPortManager with LoRa-specific configuration
        _serialPortManager = new SerialPortManager(
            "LoRa",
            "", // portName will be set later
            0, // baudRate will be set later
            loggerFactory.CreateLogger<SerialPortManager>(),
            100, // pollingIntervalMs
            2048, // readBufferSize
            20480 // maxBufferSize (20KB for LoRa)
            // Using defaults for rateUpdateIntervalMs (1000), parity (None), dataBits (8), stopBits (One)
        );

        // Subscribe to operating mode changes to control polling
        _configurationManager.OperatingModeChanged += OnOperatingModeChanged;

        // Set initial polling state based on current mode
        UpdatePollingForMode(_configurationManager.OperatingMode);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LoRa Service started - will connect on first operation");

        var lastInitAttempt = DateTime.MinValue;

        try
        {
            // Keep service running and monitor connection
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                
                // In Receive mode, try to initialize LoRa port every 5 seconds if not connected
                if (_configurationManager.OperatingMode == OperatingMode.Receive)
                {
                    if ((_serialPortManager == null || !_serialPortManager.IsConnected) && (now - lastInitAttempt).TotalSeconds >= 5)
                    {
                        _logger.LogDebug("📡 LoRa: Attempting proactive initialization for Receive mode");
                        await InitializeLoRaPort();
                        lastInitAttempt = now;
                    }
                }

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
            // Check if LoRa port exists before attempting to open
            if (!File.Exists(LORA_PORT))
            {
                _logger.LogDebug("📡 LoRa: Port {Port} does not exist - no device connected", LORA_PORT);
                return;
            }

            _logger.LogDebug("📡 LoRa: Updating SerialPortManager configuration for {Port} at {BaudRate} baud", LORA_PORT, LORA_BAUD_RATE);

            // Dispose old SerialPortManager if exists
            if (_serialPortManager != null)
            {
                _serialPortManager.DataReceived -= OnSerialDataReceived;
                _serialPortManager.Dispose();
            }

            // Create new SerialPortManager with proper port and baud rate
            _serialPortManager = new SerialPortManager(
                "LoRa",
                LORA_PORT,
                LORA_BAUD_RATE,
                _loggerFactory.CreateLogger<SerialPortManager>(),
                100, // pollingIntervalMs
                2048, // readBufferSize
                20480 // maxBufferSize (20KB for LoRa)
            );

            // Set up SerialPortManager for reliable data collection
            _serialPortManager.DataReceived += OnSerialDataReceived;
            await _serialPortManager.StartAsync();

            _logger.LogInformation("📡 LoRa: Successfully connected to {Port} with SerialPortManager", LORA_PORT);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 LoRa: Failed to open port {Port} - will retry on next operation. Check if LoRa device is connected.", LORA_PORT);
            _serialPortManager = null;
        }
    }

    private void OnSerialDataReceived(object? sender, byte[] data)
    {
        try
        {
            _loraBytesReceived += data.Length;
            _totalLoRaBytesReceived += data.Length;

            _logger.LogDebug("📡 LoRa: Received {Length} bytes - Session Total: {TotalBytes} bytes", data.Length, _totalLoRaBytesReceived);

            // Notify subscribers
            DataReceived?.Invoke(this, data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 LoRa: Error processing received data");
        }
    }

    private void OnOperatingModeChanged(object? sender, OperatingMode newMode)
    {
        try
        {
            _logger.LogInformation("📡 LoRa: Operating mode changed to {NewMode}, updating polling", newMode);
            UpdatePollingForMode(newMode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "📡 LoRa: Error handling operating mode change to {NewMode}", newMode);
        }
    }

    private void UpdatePollingForMode(OperatingMode mode)
    {
        try
        {
            bool shouldEnablePolling = mode switch
            {
                OperatingMode.Receive => true,  // Rover mode - receiving corrections
                OperatingMode.Send => false,    // Base mode - only sending corrections
                OperatingMode.Disabled => false, // No LoRa activity
                _ => false
            };

            _serialPortManager?.SetPollingEnabled(shouldEnablePolling);

            string modeDescription = mode switch
            {
                OperatingMode.Receive => "Rover (receiving corrections)",
                OperatingMode.Send => "Base (sending corrections)",
                OperatingMode.Disabled => "Disabled",
                _ => "Unknown"
            };

            _logger.LogInformation("📡 LoRa: Polling {Status} for {ModeDescription}",
                shouldEnablePolling ? "enabled" : "disabled", modeDescription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "📡 LoRa: Error updating polling for mode {Mode}", mode);
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

        // Initialize connection if needed
        if (_serialPortManager == null || !_serialPortManager.IsConnected)
        {
            _logger.LogInformation("📡 LoRa: Initializing connection to {Port}", LORA_PORT);
            await InitializeLoRaPort();
            if (_serialPortManager == null || !_serialPortManager.IsConnected)
            {
                _logger.LogWarning("📡 LoRa: Failed to establish connection");
                return;
            }
        }

        try
        {
            // Use SerialPortManager's write method
            _serialPortManager.Write(data, 0, data.Length);

            _loraBytesSent += data.Length;
            _totalLoRaBytesSent += data.Length;

            _logger.LogDebug("📡 LoRa: Successfully sent {Length} bytes - Session Total: {TotalBytes} bytes", data.Length, _totalLoRaBytesSent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📡 LoRa: Failed to send data. Device may be overwhelmed or disconnected.");
            
            // Don't try to reconnect immediately for timeout/overflow issues
            if (ex is TimeoutException || ex.Message.Contains("timeout"))
            {
                _logger.LogInformation("📡 LoRa: Skipping reconnection due to timeout - device likely overwhelmed");
                return;
            }

            // Try to reconnect for other errors
            await TryReconnectLoRaPort();
        }
    }

    private async Task TryReconnectLoRaPort()
    {
        try
        {
            _logger.LogInformation("📡 LoRa: Attempting to reconnect...");

            // Stop SerialPortManager first
            if (_serialPortManager != null)
            {
                _serialPortManager.DataReceived -= OnSerialDataReceived;
                await _serialPortManager.StopAsync();
                _serialPortManager.Dispose();
                _serialPortManager = null;
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

            //if (CurrentSendRate > 0 || CurrentReceiveRate > 0)
            //{
            //    _logger.LogInformation("📊 LoRa Rates - Send: {SendRate:F1} kbps ({SentBytes} bytes), Receive: {ReceiveRate:F1} kbps ({ReceivedBytes} bytes) in {Period:F1}s - Session Totals: {SessionSent} sent, {SessionReceived} received [Cancellation: {Cancelled}]",
            //        CurrentSendRate, totalSentBytesInPeriod, CurrentReceiveRate, totalReceivedBytesInPeriod, timeDelta, _totalLoRaBytesSent, _totalLoRaBytesReceived, stoppingToken.IsCancellationRequested);
            //}
        }
        _logger.LogDebug("UpdateDataRatesAsync completed");
    }

    public bool IsConnected => _serialPortManager?.IsConnected == true;

    public double CurrentSendRate { get; private set; } = 0.0;
    public double CurrentReceiveRate { get; private set; } = 0.0;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping LoRa Service");

        // Unsubscribe from configuration events
        _configurationManager.OperatingModeChanged -= OnOperatingModeChanged;

        // Stop SerialPortManager
        if (_serialPortManager != null)
        {
            _serialPortManager.DataReceived -= OnSerialDataReceived;
            await _serialPortManager.StopAsync();
            _logger.LogInformation("📡 LoRa SerialPortManager stopped");
        }

        await base.StopAsync(cancellationToken);

        // SerialPortManager cleanup handled above
    }

    public override void Dispose()
    {
        _logger.LogInformation("LoRa Service disposing");

        // Unsubscribe from configuration events
        _configurationManager.OperatingModeChanged -= OnOperatingModeChanged;

        // Dispose SerialPortManager
        _serialPortManager?.Dispose();

        // SerialPortManager disposal is handled above

        base.Dispose();
    }
}