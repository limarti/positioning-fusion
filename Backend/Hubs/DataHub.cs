using Microsoft.AspNetCore.SignalR;
using Backend.Hardware.Imu;
using Backend.Hardware.Gnss;
using Backend.Hardware.Gnss.Models;
using Backend.Hardware.Camera;
using Backend.GnssSystem;
using Backend.Storage;
using Backend.Configuration;
using Backend.Services;
using Backend.WiFi;

namespace Backend.Hubs;

public class DataHub : Hub
{
    private readonly ModeManagementService _modeManagementService;
    private readonly WiFiService _wifiService;
    private readonly SystemMonitoringService _systemMonitoringService;
    private readonly ImuInitializer _imuInitializer;
    private readonly GnssInitializer _gnssInitializer;
    private readonly CameraService _cameraService;
    private readonly GeoConfigurationManager _configurationManager;
    private readonly ILogger<DataHub> _logger;

    public DataHub(
        ModeManagementService modeManagementService,
        WiFiService wifiService,
        SystemMonitoringService systemMonitoringService,
        ImuInitializer imuInitializer,
        GnssInitializer gnssInitializer,
        CameraService cameraService,
        GeoConfigurationManager configurationManager,
        ILogger<DataHub> logger)
    {
        _modeManagementService = modeManagementService;
        _wifiService = wifiService;
        _systemMonitoringService = systemMonitoringService;
        _imuInitializer = imuInitializer;
        _gnssInitializer = gnssInitializer;
        _cameraService = cameraService;
        _configurationManager = configurationManager;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);

        // Immediately send hardware status to the newly connected client
        var hardwareStatus = await GetHardwareStatus();
        await Clients.Caller.SendAsync("HardwareStatusUpdate", hardwareStatus);

        await base.OnConnectedAsync();
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

    public Task<string> GetCurrentMode()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogDebug("GetCurrentMode called by client: {ConnectionId}", connectionId);

        var currentMode = _modeManagementService.CurrentMode.ToString();
        _logger.LogInformation("Returning current mode '{Mode}' to client: {ConnectionId}", currentMode, connectionId);

        return Task.FromResult(currentMode);
    }

    public async Task<bool> SetOperatingMode(string mode)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("SetOperatingMode called with mode '{Mode}' by client: {ConnectionId}", mode, connectionId);

        if (!Enum.TryParse<OperatingMode>(mode, ignoreCase: true, out var operatingMode))
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

    // WiFi Hub Methods
    public async Task<bool> ConnectToWiFi(string ssid, string password)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("ConnectToWiFi called for SSID '{SSID}' by client: {ConnectionId}", ssid, connectionId);

        try
        {
            var success = await _wifiService.ConnectToNetwork(ssid, password, true);
            _logger.LogInformation("WiFi connection to '{SSID}' {Result} for client: {ConnectionId}", 
                ssid, success ? "succeeded" : "failed", connectionId);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during WiFi connection to '{SSID}' for client: {ConnectionId}", ssid, connectionId);
            return false;
        }
    }

    public async Task<bool> SetAPConfiguration(string ssid, string password)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("SetAPConfiguration called for password update by client: {ConnectionId} (SSID parameter '{SSID}' ignored - using device name)", connectionId, ssid);

        try
        {
            // SSID is now computed from device name in WiFiService, so we just pass password
            var success = await _wifiService.SetAPPassword(password);
            _logger.LogInformation("AP password configuration {Result} for client: {ConnectionId}",
                success ? "succeeded" : "failed", connectionId);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during AP configuration for client: {ConnectionId}", connectionId);
            return false;
        }
    }

    public async Task<WiFiStatusUpdate> GetWiFiStatus()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogDebug("GetWiFiStatus called by client: {ConnectionId}", connectionId);

        try
        {
            var status = await _wifiService.GetWiFiStatus();
            _logger.LogDebug("Returning WiFi status (Mode: {Mode}, Connected: {Connected}) to client: {ConnectionId}", 
                status.CurrentMode, status.IsConnected, connectionId);
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting WiFi status for client: {ConnectionId}", connectionId);
            return new WiFiStatusUpdate
            {
                CurrentMode = WiFiMode.Disconnected,
                IsConnected = false,
                LastUpdated = DateTime.Now
            };
        }
    }

    public async Task<List<KnownWiFiNetwork>> GetKnownNetworks()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogDebug("GetKnownNetworks called by client: {ConnectionId}", connectionId);

        try
        {
            var networks = await _wifiService.GetKnownNetworks();
            _logger.LogDebug("Returning {Count} known networks to client: {ConnectionId}",
                networks.Count, connectionId);
            return networks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting known networks for client: {ConnectionId}", connectionId);
            return new List<KnownWiFiNetwork>();
        }
    }

    public WiFiAPConfiguration GetAPConfiguration()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogDebug("GetAPConfiguration called by client: {ConnectionId}", connectionId);

        var config = _wifiService.GetAPConfiguration();
        _logger.LogDebug("Returning AP configuration (SSID: {SSID}) to client: {ConnectionId}",
            config.SSID, connectionId);
        return config;
    }

    public async Task<bool> RemoveKnownNetwork(string ssid)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("RemoveKnownNetwork called for SSID '{SSID}' by client: {ConnectionId}", ssid, connectionId);

        try
        {
            await _wifiService.RemoveKnownNetwork(ssid);
            _logger.LogInformation("Known network '{SSID}' removed by client: {ConnectionId}", ssid, connectionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception removing known network '{SSID}' for client: {ConnectionId}", ssid, connectionId);
            return false;
        }
    }

    public string GetWiFiPreferredMode()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogDebug("GetWiFiPreferredMode called by client: {ConnectionId}", connectionId);

        try
        {
            var preferredMode = _wifiService.GetPreferredMode();
            _logger.LogDebug("Returning WiFi preferred mode '{Mode}' to client: {ConnectionId}", preferredMode, connectionId);
            return preferredMode.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting WiFi preferred mode for client: {ConnectionId}", connectionId);
            return WiFiMode.Client.ToString();
        }
    }

    public async Task<bool> SetWiFiPreferredMode(string mode)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("SetWiFiPreferredMode called with mode '{Mode}' by client: {ConnectionId}", mode, connectionId);

        try
        {
            if (!Enum.TryParse<WiFiMode>(mode, true, out var wifiMode))
            {
                _logger.LogWarning("Invalid WiFi mode '{Mode}' provided by client: {ConnectionId}", mode, connectionId);
                return false;
            }

            var success = await _wifiService.SetPreferredMode(wifiMode);
            _logger.LogInformation("WiFi preferred mode change to '{Mode}' {Result} for client: {ConnectionId}",
                wifiMode, success ? "succeeded" : "failed", connectionId);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception setting WiFi preferred mode to '{Mode}' for client: {ConnectionId}", mode, connectionId);
            return false;
        }
    }

    // Hostname management methods
    public async Task<string> GetCurrentHostname()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogDebug("GetCurrentHostname called by client: {ConnectionId}", connectionId);

        try
        {
            var hostname = await _systemMonitoringService.GetCurrentHostnameAsync();
            _logger.LogDebug("Returning current hostname '{Hostname}' to client: {ConnectionId}", hostname, connectionId);
            return hostname;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting current hostname for client: {ConnectionId}", connectionId);
            return "unknown";
        }
    }

    public async Task<HostnameUpdateResponse> UpdateHostname(string newHostname)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("UpdateHostname called with hostname '{Hostname}' by client: {ConnectionId}", newHostname, connectionId);

        try
        {
            var result = await _systemMonitoringService.UpdateHostnameAsync(newHostname);

            if (result.Success)
            {
                // Broadcast hostname update to all clients
                await Clients.All.SendAsync("HostnameUpdated", new {
                    hostname = result.CurrentHostname,
                    message = result.Message
                });

                _logger.LogInformation("Hostname update to '{Hostname}' succeeded for client: {ConnectionId}", newHostname, connectionId);
            }
            else
            {
                _logger.LogWarning("Hostname update to '{Hostname}' failed for client: {ConnectionId}. Error: {Error}",
                    newHostname, connectionId, result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception updating hostname to '{Hostname}' for client: {ConnectionId}", newHostname, connectionId);
            return new HostnameUpdateResponse
            {
                Success = false,
                Message = $"An error occurred while updating hostname: {ex.Message}",
                CurrentHostname = await _systemMonitoringService.GetCurrentHostnameAsync()
            };
        }
    }

    public Task<HardwareStatusUpdate> GetHardwareStatus()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogDebug("GetHardwareStatus called by client: {ConnectionId}", connectionId);

        try
        {
            var gnssStatus = _gnssInitializer.IsInitialized;
            var imuStatus = _imuInitializer.IsInitialized;
            var cameraStatus = _cameraService.IsAvailable;

            _logger.LogInformation("Hardware status check - GNSS: {Gnss}, IMU: {Imu}, Camera: {Camera}",
                gnssStatus, imuStatus, cameraStatus);

            var status = new HardwareStatusUpdate
            {
                GnssAvailable = gnssStatus,
                ImuAvailable = imuStatus,
                CameraAvailable = cameraStatus,
                EncoderAvailable = false // Always disabled for now
            };

            return Task.FromResult(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting hardware status for client: {ConnectionId}", connectionId);
            return Task.FromResult(new HardwareStatusUpdate
            {
                GnssAvailable = false,
                ImuAvailable = false,
                CameraAvailable = false,
                EncoderAvailable = false
            });
        }
    }

    // Settings management methods
    public Task<object> GetSettings()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogDebug("GetSettings called by client: {ConnectionId}", connectionId);

        try
        {
            var settings = new
            {
                surveyInDurationSeconds = _configurationManager.SurveyInDurationSeconds,
                surveyInAccuracyLimitMeters = _configurationManager.SurveyInAccuracyLimitMeters
            };

            _logger.LogDebug("Returning settings to client: {ConnectionId} - Duration: {Duration}s, Accuracy: {Accuracy}m",
                connectionId, settings.surveyInDurationSeconds, settings.surveyInAccuracyLimitMeters);

            return Task.FromResult<object>(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting settings for client: {ConnectionId}", connectionId);
            return Task.FromResult<object>(new
            {
                surveyInDurationSeconds = 10,
                surveyInAccuracyLimitMeters = 100.0
            });
        }
    }

    public async Task<bool> UpdateSettings(int surveyInDurationSeconds, double surveyInAccuracyLimitMeters)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("UpdateSettings called by client: {ConnectionId} - Duration: {Duration}s, Accuracy: {Accuracy}m",
            connectionId, surveyInDurationSeconds, surveyInAccuracyLimitMeters);

        try
        {
            _configurationManager.SurveyInDurationSeconds = surveyInDurationSeconds;
            _configurationManager.SurveyInAccuracyLimitMeters = surveyInAccuracyLimitMeters;
            _configurationManager.SaveConfiguration();

            _logger.LogInformation("Settings updated and saved successfully for client: {ConnectionId}", connectionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception updating settings for client: {ConnectionId}", connectionId);
            return false;
        }
    }

    public async Task<bool> ResetSurveyIn()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("ResetSurveyIn called by client: {ConnectionId}", connectionId);

        try
        {
            if (_configurationManager.OperatingMode != OperatingMode.SEND)
            {
                _logger.LogWarning("ResetSurveyIn called but not in SEND mode for client: {ConnectionId}", connectionId);
                return false;
            }

            var success = await _gnssInitializer.ReconfigureAsync();

            if (success)
            {
                _logger.LogInformation("Survey-In reset successfully for client: {ConnectionId}", connectionId);
            }
            else
            {
                _logger.LogWarning("Survey-In reset failed for client: {ConnectionId}", connectionId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception resetting Survey-In for client: {ConnectionId}", connectionId);
            return false;
        }
    }
}