using Backend.Hubs;
using Backend.Storage;
using Backend.GnssSystem;
using Backend.Hardware.Camera;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Battery;

public class BatteryLoggingService : BackgroundService
{
    private readonly ILogger<BatteryLoggingService> _logger;
    private readonly SystemMonitoringService _systemMonitoringService;
    private readonly CameraService _cameraService;
    private readonly DataFileWriter _dataFileWriter;
    private bool _headerWritten = false;

    public BatteryLoggingService(
        ILogger<BatteryLoggingService> logger,
        SystemMonitoringService systemMonitoringService,
        CameraService cameraService,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _systemMonitoringService = systemMonitoringService;
        _cameraService = cameraService;
        _dataFileWriter = new DataFileWriter("Battery.txt", loggerFactory.CreateLogger<DataFileWriter>());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Battery Logging Service started - logging every 10 seconds");

        // Start the data file writer
        _ = Task.Run(() => _dataFileWriter.StartAsync(stoppingToken), stoppingToken);

        var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await LogBatteryData();
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in battery logging loop");
            }
        }

        _logger.LogInformation("Battery Logging Service stopped");
    }

    private async Task LogBatteryData()
    {
        try
        {
            // Get battery data from SystemMonitoringService
            var systemHealth = await GetSystemHealthData();

            // Write CSV header if this is the first data
            if (!_headerWritten)
            {
                var csvHeader = "timestamp,battery_level,voltage,external_power_connected,camera_connected,usb_drive_connected";
                _dataFileWriter.WriteData(csvHeader);
                _headerWritten = true;
            }

            // Get camera and USB drive status
            var cameraConnected = _cameraService.IsAvailable;
            var usbDriveConnected = DataFileWriter.SharedDriveAvailable;

            // Format CSV line
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var csvLine = $"{timestamp},{systemHealth.BatteryLevel:F2},{systemHealth.BatteryVoltage:F3},{systemHealth.IsExternalPowerConnected},{cameraConnected},{usbDriveConnected}";

            _dataFileWriter.WriteData(csvLine);

            _logger.LogDebug("Battery data logged: Level={BatteryLevel:F1}%, Voltage={BatteryVoltage:F2}V, ExternalPower={IsExternalPowerConnected}, Camera={CameraConnected}, USB={UsbDriveConnected}",
                systemHealth.BatteryLevel, systemHealth.BatteryVoltage, systemHealth.IsExternalPowerConnected, cameraConnected, usbDriveConnected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging battery data");
        }
    }

    private async Task<SystemHealth> GetSystemHealthData()
    {
        // Access the GatherSystemHealth method through reflection since it's private
        // Alternatively, we could make SystemMonitoringService expose battery data publicly
        var gatherMethod = typeof(SystemMonitoringService)
            .GetMethod("GatherSystemHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (gatherMethod != null)
        {
            var task = gatherMethod.Invoke(_systemMonitoringService, null) as Task<SystemHealth>;
            if (task != null)
            {
                return await task;
            }
        }

        // Fallback: return empty data
        _logger.LogWarning("Could not access GatherSystemHealth method - returning empty data");
        return new SystemHealth
        {
            BatteryLevel = 0.0,
            BatteryVoltage = 0.0,
            IsExternalPowerConnected = false
        };
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Battery Logging Service");
        await _dataFileWriter.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _logger.LogInformation("Battery Logging Service disposing");
        _dataFileWriter.Dispose();
        base.Dispose();
    }
}
