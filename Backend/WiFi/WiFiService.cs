using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Backend.Hubs;
using Backend.Configuration;

namespace Backend.WiFi;

public class WiFiService : BackgroundService
{
    private readonly IHubContext<DataHub> _hubContext;
    private readonly GeoConfigurationManager _configManager;
    private readonly ILogger<WiFiService> _logger;
    
    private WiFiStatusUpdate _currentStatus;
    private DateTime _lastConnectionAttempt = DateTime.MinValue;
    private bool _isAttemptingConnection = false;
    private int _currentKnownNetworkIndex = 0;

    private const int CONNECTION_TIMEOUT_SECONDS = 120; // 2 minutes
    private const int STATUS_CHECK_INTERVAL_SECONDS = 30; // 30 seconds
    private const int FALLBACK_CHECK_INTERVAL_SECONDS = 10; // 10 seconds when trying to connect

    public WiFiService(IHubContext<DataHub> hubContext, GeoConfigurationManager configManager, ILogger<WiFiService> logger)
    {
        _hubContext = hubContext;
        _configManager = configManager;
        _logger = logger;
        _currentStatus = new WiFiStatusUpdate
        {
            CurrentMode = WiFiMode.Disconnected,
            IsConnected = false,
            LastUpdated = DateTime.Now
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WiFi Service starting...");

        // Initial status check
        await UpdateWiFiStatus();

        // Check if user prefers to start directly in AP mode
        var preferredMode = _configManager.WiFiConfiguration.PreferredMode;
        _logger.LogInformation("WiFi Service startup - Preferred mode is: {PreferredMode}", preferredMode);

        if (preferredMode == WiFiMode.AP)
        {
            _logger.LogInformation("Preferred mode is AP, starting in AP mode immediately");
            var apSettings = _configManager.WiFiConfiguration.APSettings;
            await ConfigureAPMode(apSettings.SSID, apSettings.Password);
        }
        else
        {
            _logger.LogInformation("Preferred mode is not AP ({PreferredMode}), skipping automatic AP configuration", preferredMode);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndUpdateWiFiStatus();

                // Use different intervals based on connection state
                var delay = _isAttemptingConnection ?
                    TimeSpan.FromSeconds(FALLBACK_CHECK_INTERVAL_SECONDS) :
                    TimeSpan.FromSeconds(STATUS_CHECK_INTERVAL_SECONDS);

                await Task.Delay(delay, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in WiFi service execution");
                await Task.Delay(TimeSpan.FromSeconds(STATUS_CHECK_INTERVAL_SECONDS), stoppingToken);
            }
        }
    }

    private async Task CheckAndUpdateWiFiStatus()
    {
        await UpdateWiFiStatus();
        
        // Check if we should attempt to connect to known networks
        if (_currentStatus.CurrentMode == WiFiMode.AP && ShouldAttemptClientConnection())
        {
            await AttemptKnownNetworkConnection();
        }
        
        // Check for connection timeout
        if (_isAttemptingConnection && 
            (DateTime.Now - _lastConnectionAttempt).TotalSeconds > CONNECTION_TIMEOUT_SECONDS)
        {
            _logger.LogWarning("Connection attempt timed out after {Timeout} seconds, falling back to AP mode", CONNECTION_TIMEOUT_SECONDS);
            await FallbackToAPMode("Connection timeout");
        }
    }

    private bool ShouldAttemptClientConnection()
    {
        var wifiConfig = _configManager.WiFiConfiguration;
        var knownNetworks = wifiConfig.KnownNetworks;

        // Only attempt client connection if preferred mode is Client and we have known networks
        return wifiConfig.PreferredMode == WiFiMode.Client &&
               knownNetworks.Any() &&
               !_isAttemptingConnection;
    }

    private async Task AttemptKnownNetworkConnection()
    {
        var knownNetworks = _configManager.WiFiConfiguration.KnownNetworks
            .OrderByDescending(n => n.LastConnected)
            .ToList();

        if (!knownNetworks.Any())
            return;

        var network = knownNetworks[_currentKnownNetworkIndex % knownNetworks.Count];
        _currentKnownNetworkIndex++;

        _logger.LogInformation("Attempting to connect to known network: {SSID}", network.SSID);
        await ConnectToNetwork(network.SSID, network.Password, false);
    }

    public async Task<bool> ConnectToNetwork(string ssid, string password, bool saveToKnown = true)
    {
        _logger.LogInformation("Connecting to WiFi network: {SSID}", ssid);

        _isAttemptingConnection = true;
        _lastConnectionAttempt = DateTime.Now;

        try
        {
            var result = await ExecuteNmcliCommand($"device wifi connect \"{ssid}\" password \"{password}\"");

            if (result.Success)
            {
                _logger.LogInformation("Successfully connected to {SSID}", ssid);

                // Update preferred mode to Client since user manually connected to a network
                if (_configManager.WiFiConfiguration.PreferredMode != WiFiMode.Client)
                {
                    _logger.LogInformation("Setting preferred mode to Client due to manual network connection");
                    _configManager.WiFiConfiguration.PreferredMode = WiFiMode.Client;
                    _configManager.SaveWiFiConfiguration();
                }

                if (saveToKnown)
                {
                    await AddToKnownNetworks(ssid, password);
                }

                _isAttemptingConnection = false;
                await UpdateWiFiStatus();
                return true;
            }
            else
            {
                _logger.LogWarning("Failed to connect to {SSID}: {Error}", ssid, result.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception connecting to {SSID}", ssid);
            return false;
        }
    }

    public async Task<bool> ConfigureAPMode(string ssid, string password)
    {
        _logger.LogInformation("Configuring AP mode: SSID={SSID}", ssid);

        try
        {
            var apSettings = _configManager.WiFiConfiguration.APSettings;
            apSettings.SSID = ssid;
            apSettings.Password = password;

            _configManager.SaveWiFiConfiguration();

            // First, check if connection exists and delete it to start fresh
            _logger.LogInformation("Removing existing AP connection if it exists");
            var deleteResult = await ExecuteNmcliCommand($"con delete \"{ssid}\"");
            if (deleteResult.Success)
            {
                _logger.LogInformation("Deleted existing connection: {SSID}", ssid);
            }

            // Get the WiFi interface name first
            _logger.LogInformation("Finding WiFi interface...");
            var wifiInterface = await GetWiFiInterfaceName();
            if (string.IsNullOrEmpty(wifiInterface))
            {
                _logger.LogError("No WiFi interface found for AP configuration");
                return false;
            }
            _logger.LogInformation("Using WiFi interface: {Interface}", wifiInterface);

            // Create new AP connection with specific interface
            _logger.LogInformation("Creating new AP connection: {SSID}", ssid);
            var createCommand = $"con add type wifi ifname {wifiInterface} con-name \"{ssid}\" autoconnect yes ssid \"{ssid}\" " +
                               $"802-11-wireless.mode ap 802-11-wireless.band bg " +
                               $"wifi-sec.key-mgmt wpa-psk wifi-sec.psk \"{password}\" " +
                               $"ipv4.method shared ipv4.addresses {apSettings.IPAddress}/{apSettings.Subnet} " +
                               $"connection.autoconnect-priority 100";

            var createResult = await ExecuteNmcliCommand(createCommand);
            if (!createResult.Success)
            {
                _logger.LogError("Failed to create AP connection: {Error}", createResult.Error);
                return false;
            }

            // Bring up the connection
            _logger.LogInformation("Bringing up AP connection: {SSID}", ssid);
            var upResult = await ExecuteNmcliCommand($"con up \"{ssid}\"");
            if (!upResult.Success)
            {
                _logger.LogWarning("Failed to bring up AP connection: {Error}", upResult.Error);
            }

            await UpdateWiFiStatus();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception configuring AP mode");
            return false;
        }
    }

    private async Task FallbackToAPMode(string reason)
    {
        _logger.LogInformation("Falling back to AP mode. Reason: {Reason}", reason);
        
        _isAttemptingConnection = false;
        
        var apSettings = _configManager.WiFiConfiguration.APSettings;
        await ConfigureAPMode(apSettings.SSID, apSettings.Password);
        
        var notification = new WiFiFallbackNotification
        {
            Message = $"Switched to AP mode: {reason}",
            Reason = reason,
            Timestamp = DateTime.Now
        };
        
        await _hubContext.Clients.All.SendAsync("WiFiFallbackNotification", notification);
    }

    private async Task AddToKnownNetworks(string ssid, string password)
    {
        var knownNetworks = _configManager.WiFiConfiguration.KnownNetworks;
        
        var existing = knownNetworks.FirstOrDefault(n => n.SSID == ssid);
        if (existing != null)
        {
            existing.Password = password;
            existing.LastConnected = DateTime.Now;
        }
        else
        {
            knownNetworks.Add(new StoredWiFiNetwork
            {
                SSID = ssid,
                Password = password,
                LastConnected = DateTime.Now
            });
        }
        
        _configManager.SaveWiFiConfiguration();
        await SendKnownNetworksUpdate();
    }

    public async Task RemoveKnownNetwork(string ssid)
    {
        var knownNetworks = _configManager.WiFiConfiguration.KnownNetworks;
        var network = knownNetworks.FirstOrDefault(n => n.SSID == ssid);
        
        if (network != null)
        {
            knownNetworks.Remove(network);
            _configManager.SaveWiFiConfiguration();
            await SendKnownNetworksUpdate();
            _logger.LogInformation("Removed known network: {SSID}", ssid);
        }
    }

    private async Task UpdateWiFiStatus()
    {
        var status = await GetCurrentWiFiStatus();
        
        if (status.CurrentMode != _currentStatus.CurrentMode || 
            status.ConnectedNetworkSSID != _currentStatus.ConnectedNetworkSSID ||
            status.IsConnected != _currentStatus.IsConnected)
        {
            _currentStatus = status;
            await _hubContext.Clients.All.SendAsync("WiFiStatusUpdate", status);
        }
    }

    private async Task<WiFiStatusUpdate> GetCurrentWiFiStatus()
    {
        try
        {
            var result = await ExecuteNmcliCommand("device wifi");
            var connectionResult = await ExecuteNmcliCommand("connection show --active");
            
            var status = new WiFiStatusUpdate
            {
                LastUpdated = DateTime.Now
            };

            // Check if we're in AP mode or connected as client
            if (connectionResult.Success && connectionResult.Output.Contains("wifi"))
            {
                var lines = connectionResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("wifi") && line.Contains("802-11-wireless"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            var connectionName = parts[0];
                            
                            // Check if this is our AP connection
                            var apSettings = _configManager.WiFiConfiguration.APSettings;
                            if (connectionName.Contains(apSettings.SSID) || line.Contains("ap"))
                            {
                                status.CurrentMode = WiFiMode.AP;
                                status.ConnectedNetworkSSID = apSettings.SSID;
                                status.IsConnected = true;
                            }
                            else
                            {
                                status.CurrentMode = WiFiMode.Client;
                                status.ConnectedNetworkSSID = connectionName;
                                status.IsConnected = true;
                                
                                // Try to get signal strength
                                status.SignalStrength = await GetSignalStrength(connectionName);
                            }
                        }
                    }
                }
            }
            else
            {
                status.CurrentMode = WiFiMode.Disconnected;
                status.IsConnected = false;
            }

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting WiFi status");
            return new WiFiStatusUpdate
            {
                CurrentMode = WiFiMode.Disconnected,
                IsConnected = false,
                LastUpdated = DateTime.Now
            };
        }
    }

    private async Task<int?> GetSignalStrength(string ssid)
    {
        try
        {
            var result = await ExecuteNmcliCommand($"device wifi list");
            if (result.Success)
            {
                var lines = result.Output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains(ssid) && line.Contains("*"))
                    {
                        var signalMatch = Regex.Match(line, @"(\d+)%");
                        if (signalMatch.Success && int.TryParse(signalMatch.Groups[1].Value, out var signal))
                        {
                            return signal;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get signal strength for {SSID}", ssid);
        }
        return null;
    }

    private async Task SendKnownNetworksUpdate()
    {
        var update = new WiFiKnownNetworksUpdate
        {
            Networks = _configManager.WiFiConfiguration.KnownNetworks.Select(n => new KnownWiFiNetwork
            {
                SSID = n.SSID,
                LastConnected = n.LastConnected
            }).ToList(),
            LastUpdated = DateTime.Now
        };
        
        await _hubContext.Clients.All.SendAsync("WiFiKnownNetworksUpdate", update);
    }

    private async Task<string> GetWiFiInterfaceName()
    {
        try
        {
            var result = await ExecuteNmcliCommand("device status");
            if (result.Success)
            {
                var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("wifi") && !line.Contains("unavailable"))
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            return parts[0]; // First column is the interface name
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting WiFi interface name");
        }

        return null; // No WiFi interface found
    }

    private async Task<(bool Success, string Output, string Error)> ExecuteNmcliCommand(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "nmcli",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _logger.LogDebug("Executing: nmcli {Arguments}", arguments);
            
            process.Start();
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            
            var output = await outputTask;
            var error = await errorTask;
            
            var success = process.ExitCode == 0;
            
            if (!success)
            {
                _logger.LogWarning("nmcli command failed: {Arguments}, Exit Code: {ExitCode}, Error: {Error}", 
                    arguments, process.ExitCode, error);
            }
            else
            {
                _logger.LogDebug("nmcli command succeeded: {Arguments}", arguments);
            }
            
            return (success, output, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception executing nmcli command: {Arguments}", arguments);
            return (false, string.Empty, ex.Message);
        }
    }

    public async Task<WiFiStatusUpdate> GetWiFiStatus()
    {
        await UpdateWiFiStatus();
        return _currentStatus;
    }

    public async Task<List<KnownWiFiNetwork>> GetKnownNetworks()
    {
        return _configManager.WiFiConfiguration.KnownNetworks.Select(n => new KnownWiFiNetwork
        {
            SSID = n.SSID,
            LastConnected = n.LastConnected
        }).ToList();
    }

    public WiFiMode GetPreferredMode()
    {
        return _configManager.WiFiConfiguration.PreferredMode;
    }

    public async Task<bool> SetPreferredMode(WiFiMode preferredMode)
    {
        try
        {
            _logger.LogInformation("Setting preferred WiFi mode to: {Mode}", preferredMode);

            _configManager.WiFiConfiguration.PreferredMode = preferredMode;
            _configManager.SaveWiFiConfiguration();

            // Switch modes immediately based on preference
            if (preferredMode == WiFiMode.AP)
            {
                _logger.LogInformation("Preferred mode changed to AP, switching to AP mode immediately");
                _isAttemptingConnection = false; // Stop any client connection attempts
                var apSettings = _configManager.WiFiConfiguration.APSettings;
                await ConfigureAPMode(apSettings.SSID, apSettings.Password);
            }
            else if (preferredMode == WiFiMode.Client)
            {
                _logger.LogInformation("Preferred mode changed to Client, attempting to connect to known networks");
                _isAttemptingConnection = false; // Reset connection attempt state
                await AttemptKnownNetworkConnection();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set preferred WiFi mode to: {Mode}", preferredMode);
            return false;
        }
    }
}