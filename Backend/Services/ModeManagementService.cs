using Backend.Configuration;
using Backend.Hubs;
using Backend.Hardware.Gnss;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Services;

public class ModeManagementService : BackgroundService
{
    private readonly ILogger<ModeManagementService> _logger;
    private readonly GeoConfigurationManager _configManager;
    private readonly IHubContext<DataHub> _hubContext;
    private readonly GnssInitializer _gnssInitializer;
    private OperatingMode _currentMode;

    public ModeManagementService(
        ILogger<ModeManagementService> logger,
        GeoConfigurationManager configManager,
        IHubContext<DataHub> hubContext,
        GnssInitializer gnssInitializer)
    {
        _logger = logger;
        _configManager = configManager;
        _hubContext = hubContext;
        _gnssInitializer = gnssInitializer;
        _currentMode = _configManager.OperatingMode;

        _logger.LogInformation("ModeManagementService initialized with mode: {Mode}", _currentMode);
    }

    public OperatingMode CurrentMode => _currentMode;

    public async Task<bool> SetOperatingModeAsync(OperatingMode newMode)
    {
        var oldMode = _currentMode;

        _logger.LogInformation("Mode change requested: {OldMode} → {NewMode}", oldMode, newMode);

        if (oldMode == newMode)
        {
            _logger.LogInformation("Mode change skipped - already in {Mode} mode", newMode);
            return true;
        }

        try
        {
            _logger.LogDebug("Updating internal mode state from {OldMode} to {NewMode}", oldMode, newMode);
            _currentMode = newMode;

            _logger.LogDebug("Updating configuration manager with new mode: {NewMode}", newMode);
            _configManager.OperatingMode = newMode;

            _logger.LogDebug("Saving operating mode configuration to disk");
            _configManager.SaveConfiguration();

            _logger.LogInformation("Operating mode successfully changed from {OldMode} to {NewMode}", oldMode, newMode);

            // Reconfigure GNSS hardware for the new mode
            _logger.LogDebug("Reconfiguring GNSS hardware for new mode: {NewMode}", newMode);
            try
            {
                var gnssReconfigured = await _gnssInitializer.ReconfigureAsync();
                if (gnssReconfigured)
                {
                    _logger.LogInformation("GNSS hardware reconfiguration completed for mode: {NewMode}", newMode);
                }
                else
                {
                    _logger.LogWarning("GNSS hardware reconfiguration failed for mode: {NewMode} - continuing anyway", newMode);
                }
            }
            catch (Exception gnssEx)
            {
                _logger.LogError(gnssEx, "Exception during GNSS reconfiguration for mode: {NewMode} - continuing anyway", newMode);
            }

            // Broadcast mode change to all connected clients
            _logger.LogDebug("Broadcasting mode change to all SignalR clients");
            await _hubContext.Clients.All.SendAsync("ModeChanged", new
            {
                Mode = newMode.ToString(),
                Timestamp = DateTime.UtcNow
            });

            _logger.LogInformation("Mode change broadcast completed for {NewMode}", newMode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change operating mode from {OldMode} to {NewMode}", oldMode, newMode);

            // Revert internal state on failure
            _currentMode = oldMode;
            _logger.LogWarning("Reverted internal mode state back to {OldMode} due to failure", oldMode);

            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ModeManagementService background task started");

        // Send initial mode status after a short delay to ensure SignalR is ready
        _logger.LogDebug("Waiting 2 seconds before broadcasting initial mode to ensure SignalR is ready");
        await Task.Delay(2000, stoppingToken);

        try
        {
            _logger.LogDebug("Broadcasting initial operating mode: {Mode}", _currentMode);
            await _hubContext.Clients.All.SendAsync("ModeChanged", new
            {
                Mode = _currentMode.ToString(),
                Timestamp = DateTime.UtcNow
            }, stoppingToken);

            _logger.LogInformation("Initial operating mode broadcast completed: {Mode}", _currentMode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast initial operating mode: {Mode}", _currentMode);
        }

        _logger.LogDebug("ModeManagementService entering maintenance loop");

        // Keep the service running
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _logger.LogInformation("ModeManagementService background task stopped");
    }
}