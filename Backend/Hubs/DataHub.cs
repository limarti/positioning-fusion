using Microsoft.AspNetCore.SignalR;
using Backend.Hardware.Imu;
using Backend.Hardware.Gnss;
using Backend.Hardware.Camera;
using Backend.GnssSystem;
using Backend.Storage;
using Backend.Configuration;
using Backend.Services;

namespace Backend.Hubs;

public class DataHub : Hub
{
    private readonly ModeManagementService _modeManagementService;
    private readonly ILogger<DataHub> _logger;

    public DataHub(ModeManagementService modeManagementService, ILogger<DataHub> logger)
    {
        _modeManagementService = modeManagementService;
        _logger = logger;
    }
    public async Task SendImuUpdate(ImuData imuData)
    {
        await Clients.All.SendAsync("ImuUpdate", new ImuUpdate
        {
            Timestamp = imuData.Timestamp,
            Acceleration = new Vector3Update { X = imuData.Acceleration.X, Y = imuData.Acceleration.Y, Z = imuData.Acceleration.Z },
            Gyroscope = new Vector3Update { X = imuData.Gyroscope.X, Y = imuData.Gyroscope.Y, Z = imuData.Gyroscope.Z },
            Magnetometer = new Vector3Update { X = imuData.Magnetometer.X, Y = imuData.Magnetometer.Y, Z = imuData.Magnetometer.Z }
        });
    }

    public async Task SendSystemHealthUpdate(SystemHealth systemHealth)
    {
        await Clients.All.SendAsync("SystemHealthUpdate", new SystemHealthUpdate
        {
            CpuUsage = systemHealth.CpuUsage,
            MemoryUsage = systemHealth.MemoryUsage,
            Temperature = systemHealth.Temperature
        });
    }

    public async Task SendDataRatesUpdate(DataRatesUpdate dataRates)
    {
        await Clients.All.SendAsync("DataRatesUpdate", dataRates);
    }

    public async Task SendFileLoggingStatusUpdate(FileLoggingStatus status)
    {
        await Clients.All.SendAsync("FileLoggingStatusUpdate", status);
    }

    public async Task SendCameraUpdate(CameraUpdate cameraUpdate)
    {
        await Clients.All.SendAsync("CameraUpdate", cameraUpdate);
    }

    public async Task<string> GetCurrentMode()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogDebug("GetCurrentMode called by client: {ConnectionId}", connectionId);

        var currentMode = _modeManagementService.CurrentMode.ToString();
        _logger.LogInformation("Returning current mode '{Mode}' to client: {ConnectionId}", currentMode, connectionId);

        return currentMode;
    }

    public async Task<bool> SetOperatingMode(string mode)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("SetOperatingMode called with mode '{Mode}' by client: {ConnectionId}", mode, connectionId);

        if (!Enum.TryParse<OperatingMode>(mode, true, out var operatingMode))
        {
            _logger.LogWarning("Invalid operating mode '{Mode}' provided by client: {ConnectionId}", mode, connectionId);
            return false;
        }

        _logger.LogDebug("Parsed operating mode: {OperatingMode} for client: {ConnectionId}", operatingMode, connectionId);

        var success = await _modeManagementService.SetOperatingModeAsync(operatingMode);

        if (success)
        {
            _logger.LogInformation("Operating mode change to '{Mode}' succeeded for client: {ConnectionId}", operatingMode, connectionId);
        }
        else
        {
            _logger.LogWarning("Operating mode change to '{Mode}' failed for client: {ConnectionId}", operatingMode, connectionId);
        }

        return success;
    }
}