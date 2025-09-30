using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Backend.Hubs;
using Backend.Configuration;

namespace Backend.WiFi;

public class WiFiService : BackgroundService
{
    // AP network constants
    private const string AP_IP_ADDRESS = "10.200.1.1";
    private const string AP_SUBNET = "24";

    private readonly IHubContext<DataHub> _hubContext;
    private readonly GeoConfigurationManager _configManager;
    private readonly ILogger<WiFiService> _logger;
    
    private WiFiStatusUpdate _currentStatus;
    private DateTime _lastConnectionAttempt = DateTime.MinValue;
    private DateTime _lastSuccessfulConnection = DateTime.MinValue;
    private bool _isAttemptingConnection = false;
    private int _currentKnownNetworkIndex = 0;

    private const int CONNECTION_TIMEOUT_SECONDS = 120; // 2 minutes
    private const int STATUS_CHECK_INTERVAL_SECONDS = 30; // 30 seconds
    private const int FALLBACK_CHECK_INTERVAL_SECONDS = 10; // 10 seconds when trying to connect
    private const int CLIENT_RETRY_INTERVAL_SECONDS = 120; // 2 minutes - retry client connection from AP mode
    private const int CONNECTION_COOLDOWN_SECONDS = 60; // 1 minute cooldown after successful connection

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

    // ===========================================================================================
    // BACKGROUND SERVICE LIFECYCLE
    // ===========================================================================================

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
            var apPassword = _configManager.WiFiConfiguration.APSettings.Password;
            await ConfigureAPMode(_configManager.APName, apPassword);
        }
        else if (preferredMode == WiFiMode.Client)
        {
            _logger.LogInformation("Preferred mode is Client, attempting to connect to known networks on startup");
            await AttemptKnownNetworkConnection();
        }
        else
        {
            _logger.LogInformation("Preferred mode is ({PreferredMode}), no automatic configuration", preferredMode);
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

    // ===========================================================================================
    // CONNECTION MANAGEMENT - CLIENT MODE
    // ===========================================================================================

    private async Task CheckAndUpdateWiFiStatus()
    {
        await UpdateWiFiStatus();

        // Check if we should attempt to connect to known networks
        if (ShouldAttemptClientConnection())
        {
            await AttemptKnownNetworkConnection();
        }

        // Check if we should retry client connection from AP mode with no clients
        if (await ShouldRetryClientFromAP())
        {
            _logger.LogInformation("AP mode active with no connected clients, attempting to reconnect as client");
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

        // Check if we're in cooldown period after successful connection
        var timeSinceLastSuccess = (DateTime.Now - _lastSuccessfulConnection).TotalSeconds;
        var inCooldown = timeSinceLastSuccess < CONNECTION_COOLDOWN_SECONDS;

        var shouldAttempt = wifiConfig.PreferredMode == WiFiMode.Client &&
                           knownNetworks.Any() &&
                           !_isAttemptingConnection &&
                           !(_currentStatus.CurrentMode == WiFiMode.Client && _currentStatus.IsConnected) &&
                           _currentStatus.CurrentMode != WiFiMode.AP && // Don't attempt if in AP mode - let ShouldRetryClientFromAP handle that
                           !inCooldown;

        // Debug logging to understand why connections are being attempted
        if (wifiConfig.PreferredMode == WiFiMode.Client)
        {
            if (!shouldAttempt)
            {
                _logger.LogDebug("Not attempting client connection - PreferredMode: {PreferredMode}, HasKnownNetworks: {HasKnownNetworks}, IsAttempting: {IsAttempting}, CurrentMode: {CurrentMode}, IsConnected: {IsConnected}, InCooldown: {InCooldown} (TimeSinceLastSuccess: {TimeSinceLastSuccess}s)",
                    wifiConfig.PreferredMode, knownNetworks.Any(), _isAttemptingConnection, _currentStatus.CurrentMode, _currentStatus.IsConnected, inCooldown, timeSinceLastSuccess);
            }
            else
            {
                _logger.LogInformation("Attempting client connection - CurrentMode: {CurrentMode}, IsConnected: {IsConnected}, TimeSinceLastSuccess: {TimeSinceLastSuccess}s",
                    _currentStatus.CurrentMode, _currentStatus.IsConnected, timeSinceLastSuccess);
            }
        }

        return shouldAttempt;
    }

    private async Task<bool> ShouldRetryClientFromAP()
    {
        var wifiConfig = _configManager.WiFiConfiguration;

        // Only retry if:
        // 1. Preferred mode is Client
        // 2. Currently in AP mode and connected
        // 3. No clients connected to AP
        // 4. Not currently attempting a connection
        // 5. Enough time has passed since last retry
        // 6. Have known networks to try
        var shouldRetry = wifiConfig.PreferredMode == WiFiMode.Client &&
                         _currentStatus.CurrentMode == WiFiMode.AP &&
                         _currentStatus.IsConnected &&
                         !await HasConnectedAPClients() &&
                         !_isAttemptingConnection &&
                         (DateTime.Now - _lastConnectionAttempt).TotalSeconds >= CLIENT_RETRY_INTERVAL_SECONDS &&
                         wifiConfig.KnownNetworks.Any();

        if (shouldRetry)
        {
            _logger.LogDebug("Should retry client from AP - PreferredMode: {PreferredMode}, CurrentMode: {CurrentMode}, IsConnected: {IsConnected}, HasAPClients: {HasAPClients}, IsAttempting: {IsAttempting}, TimeSinceLastRetry: {TimeSinceLastRetry}s, HasKnownNetworks: {HasKnownNetworks}",
                wifiConfig.PreferredMode, _currentStatus.CurrentMode, _currentStatus.IsConnected,
                await HasConnectedAPClients(), _isAttemptingConnection,
                (DateTime.Now - _lastConnectionAttempt).TotalSeconds, wifiConfig.KnownNetworks.Any());
        }

        return shouldRetry;
    }

    private async Task<bool> HasConnectedAPClients()
    {
        try
        {
            // Use iw to check connected stations to our AP interface
            var wifiInterface = await GetWiFiInterfaceName();
            if (string.IsNullOrEmpty(wifiInterface))
            {
                _logger.LogDebug("No WiFi interface found for AP client check");
                return false;
            }

            var result = await ExecuteIwCommand($"dev {wifiInterface} station dump");

            if (result.Success)
            {
                // If station dump has any output, we have connected clients
                var hasClients = !string.IsNullOrWhiteSpace(result.Output);
                _logger.LogDebug("AP client check - Interface: {Interface}, HasClients: {HasClients}", wifiInterface, hasClients);
                return hasClients;
            }
            else
            {
                _logger.LogDebug("Failed to check AP clients: {Error}", result.Error);
                return false; // Assume no clients if we can't check
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception checking AP clients");
            return false; // Assume no clients if error occurs
        }
    }

    private async Task<(bool Success, string Output, string Error)> ExecuteIwCommand(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "iw",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _logger.LogDebug("Executing: iw {Arguments}", arguments);

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;

            var success = process.ExitCode == 0;

            if (!success)
            {
                _logger.LogDebug("iw command failed: {Arguments}, Exit Code: {ExitCode}, Error: {Error}",
                    arguments, process.ExitCode, error);
            }
            else
            {
                _logger.LogDebug("iw command succeeded: {Arguments}", arguments);
            }

            return (success, output, error);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Exception executing iw command: {Arguments}", arguments);
            return (false, string.Empty, ex.Message);
        }
    }

    private async Task AttemptKnownNetworkConnection()
    {
        // Double-check we're not already connected before attempting
        var currentStatus = await GetCurrentWiFiStatus();
        if (currentStatus.CurrentMode == WiFiMode.Client && currentStatus.IsConnected)
        {
            _logger.LogDebug("Skipping known network connection attempt - already connected as client to {SSID}", currentStatus.ConnectedNetworkSSID);
            _isAttemptingConnection = false;
            _lastSuccessfulConnection = DateTime.Now; // Update cooldown
            return;
        }

        var knownNetworks = _configManager.WiFiConfiguration.KnownNetworks
            .OrderByDescending(n => n.LastConnected)
            .ToList();

        if (!knownNetworks.Any())
            return;

        // Check if we're already connected to one of the known networks
        if (!string.IsNullOrEmpty(currentStatus.ConnectedNetworkSSID))
        {
            var alreadyConnectedToKnown = knownNetworks.Any(n => n.SSID == currentStatus.ConnectedNetworkSSID);
            if (alreadyConnectedToKnown)
            {
                _logger.LogDebug("Already connected to known network {SSID}, skipping connection attempt", currentStatus.ConnectedNetworkSSID);
                _isAttemptingConnection = false;
                _lastSuccessfulConnection = DateTime.Now; // Update cooldown
                return;
            }
        }

        // Start the 2-minute retry period if not already attempting
        if (!_isAttemptingConnection)
        {
            _isAttemptingConnection = true;
            _lastConnectionAttempt = DateTime.Now;
            _currentKnownNetworkIndex = 0;
            _logger.LogInformation("Starting 2-minute connection attempt period for known networks");
        }

        var network = knownNetworks[_currentKnownNetworkIndex % knownNetworks.Count];
        _currentKnownNetworkIndex++;

        _logger.LogInformation("Attempting to connect to known network: {SSID} (attempt in 2-minute window)", network.SSID);
        var success = await ConnectToNetworkDuringRetry(network.SSID, network.Password);

        if (success)
        {
            _logger.LogInformation("Successfully connected to {SSID} during retry period", network.SSID);
            _isAttemptingConnection = false;
            _lastSuccessfulConnection = DateTime.Now;
        }
    }

    private async Task<bool> ConnectToNetworkDuringRetry(string ssid, string password)
    {
        try
        {
            var result = await ExecuteNmcliCommand(BuildWiFiConnectCommand(ssid, password));

            if (result.Success)
            {
                _logger.LogInformation("Successfully connected to {SSID} during retry period", ssid);
                _lastSuccessfulConnection = DateTime.Now;

                // Update preferred mode to Client
                if (_configManager.WiFiConfiguration.PreferredMode != WiFiMode.Client)
                {
                    _logger.LogInformation("Setting preferred mode to Client due to successful connection");
                    _configManager.WiFiConfiguration.PreferredMode = WiFiMode.Client;
                    _configManager.SaveConfiguration();
                }

                // Update the LastConnected time for this network
                var knownNetworks = _configManager.WiFiConfiguration.KnownNetworks;
                var existingNetwork = knownNetworks.FirstOrDefault(n => n.SSID == ssid);
                if (existingNetwork != null)
                {
                    existingNetwork.LastConnected = DateTime.Now;
                    _configManager.SaveConfiguration();
                }

                await UpdateWiFiStatus();
                return true;
            }
            else
            {
                _logger.LogDebug("Failed to connect to {SSID} during retry period: {Error}", ssid, result.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception connecting to {SSID} during retry period", ssid);
            return false;
        }
    }

    public async Task<bool> ConnectToNetwork(string ssid, string password, bool saveToKnown = true)
    {
        _logger.LogInformation("Connecting to WiFi network: {SSID}", ssid);

        _isAttemptingConnection = true;
        _lastConnectionAttempt = DateTime.Now;

        try
        {
            var result = await ExecuteNmcliCommand(BuildWiFiConnectCommand(ssid, password));

            if (result.Success)
            {
                _logger.LogInformation("Successfully connected to {SSID}", ssid);
                _lastSuccessfulConnection = DateTime.Now;

                // Update preferred mode to Client since user manually connected to a network
                if (_configManager.WiFiConfiguration.PreferredMode != WiFiMode.Client)
                {
                    _logger.LogInformation("Setting preferred mode to Client due to manual network connection");
                    _configManager.WiFiConfiguration.PreferredMode = WiFiMode.Client;
                    _configManager.SaveConfiguration();
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
                _isAttemptingConnection = false;
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception connecting to {SSID}", ssid);
            _isAttemptingConnection = false;
            return false;
        }
    }

    // ===========================================================================================
    // ACCESS POINT (AP) MODE MANAGEMENT
    // ===========================================================================================

    public async Task<bool> ConfigureAPMode(string ssid, string password)
    {
        _logger.LogInformation("Configuring AP mode: SSID={SSID}", ssid);

        try
        {
            var apSettings = _configManager.WiFiConfiguration.APSettings;
            apSettings.Password = password;

            _configManager.SaveConfiguration();

            // Check if AP is already running with the same SSID
            var currentStatus = await GetCurrentWiFiStatus();
            if (currentStatus.CurrentMode == WiFiMode.AP && 
                currentStatus.IsConnected && 
                currentStatus.ConnectedNetworkSSID == ssid)
            {
                _logger.LogDebug("AP mode already configured and running with SSID: {SSID}", ssid);
                return true;
            }

            // First, check if connection exists and delete it to start fresh
            _logger.LogInformation("Removing existing AP connection if it exists");
            var deleteResult = await ExecuteNmcliCommand(BuildConnectionDeleteCommand(ssid));
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
            var createCommand = BuildAPConnectionCommand(wifiInterface, ssid, password);

            var createResult = await ExecuteNmcliCommand(createCommand);
            if (!createResult.Success)
            {
                _logger.LogError("Failed to create AP connection: {Error}", createResult.Error);
                return false;
            }

            // Bring up the connection
            _logger.LogInformation("Bringing up AP connection: {SSID}", ssid);
            var upResult = await ExecuteNmcliCommand(BuildConnectionUpCommand(ssid));
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
        
        // Check if we're already in AP mode to avoid unnecessary reconfiguration
        var currentStatus = await GetCurrentWiFiStatus();
        if (currentStatus.CurrentMode == WiFiMode.AP && currentStatus.IsConnected)
        {
            _logger.LogDebug("Already in AP mode, skipping fallback reconfiguration");
            return;
        }
        
        var apPassword = _configManager.WiFiConfiguration.APSettings.Password;
        await ConfigureAPMode(_configManager.APName, apPassword);
        
        var notification = new WiFiFallbackNotification
        {
            Message = $"Switched to AP mode: {reason}",
            Reason = reason,
            Timestamp = DateTime.Now
        };
        
        await _hubContext.Clients.All.SendAsync("WiFiFallbackNotification", notification);
    }

    // ===========================================================================================
    // KNOWN NETWORKS MANAGEMENT
    // ===========================================================================================

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
        
        _configManager.SaveConfiguration();
        await SendKnownNetworksUpdate();
    }

    public async Task RemoveKnownNetwork(string ssid)
    {
        var knownNetworks = _configManager.WiFiConfiguration.KnownNetworks;
        var network = knownNetworks.FirstOrDefault(n => n.SSID == ssid);
        
        if (network != null)
        {
            knownNetworks.Remove(network);
            _configManager.SaveConfiguration();
            await SendKnownNetworksUpdate();
            _logger.LogInformation("Removed known network: {SSID}", ssid);
        }
    }

    // ===========================================================================================
    // STATUS MONITORING AND SIGNALR UPDATES
    // ===========================================================================================

    private async Task UpdateWiFiStatus()
    {
        var status = await GetCurrentWiFiStatus();
        
        if (status.CurrentMode != _currentStatus.CurrentMode || 
            status.ConnectedNetworkSSID != _currentStatus.ConnectedNetworkSSID ||
            status.IsConnected != _currentStatus.IsConnected)
        {
            await _hubContext.Clients.All.SendAsync("WiFiStatusUpdate", status);
        }
        
        // Always update current status to ensure it reflects actual state
        _currentStatus = status;
    }

    private async Task<WiFiStatusUpdate> GetCurrentWiFiStatus()
    {
        try
        {
            // Use device status which is more reliable than parsing connection output
            var deviceResult = await ExecuteNmcliCommand(BuildDeviceStatusQuery());
            
            var status = new WiFiStatusUpdate
            {
                LastUpdated = DateTime.Now
            };

            _logger.LogDebug("GetCurrentWiFiStatus - deviceResult.Success: {Success}", deviceResult.Success);

            if (deviceResult.Success)
            {
                var lines = deviceResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    _logger.LogDebug("Checking device line: {Line}", line);

                    // Look for wifi device that's connected
                    if (IsWiFiConnectedLine(line))
                    {
                        var (deviceName, deviceType, state, connectionName) = ParseDeviceStatusLine(line);
                        if (!string.IsNullOrEmpty(deviceName))
                        {
                            _logger.LogDebug("Found wifi device - Device: {Device}, State: {State}, Connection: {Connection}",
                                deviceName, state, connectionName);

                            if (state == "connected" && !string.IsNullOrEmpty(connectionName))
                            {
                                // Check if this is our AP connection
                                var apName = _configManager.APName;
                                if (connectionName.Contains(apName))
                                {
                                    status.CurrentMode = WiFiMode.AP;
                                    status.ConnectedNetworkSSID = apName;
                                    status.IsConnected = true;
                                    _logger.LogDebug("Detected AP mode - SSID: {SSID}", apName);
                                }
                                else
                                {
                                    status.CurrentMode = WiFiMode.Client;
                                    status.ConnectedNetworkSSID = connectionName;
                                    status.IsConnected = true;
                                    _logger.LogDebug("Detected Client mode - ConnectionName: {ConnectionName}", connectionName);
                                    
                                    // Try to get signal strength
                                    status.SignalStrength = await GetSignalStrength(connectionName);
                                }
                                
                                break; // Found our wifi connection, stop looking
                            }
                        }
                    }
                }
            }

            // If no connected wifi device found, we're disconnected
            if (!status.IsConnected)
            {
                status.CurrentMode = WiFiMode.Disconnected;
                status.IsConnected = false;
                _logger.LogDebug("Detected Disconnected mode");
            }

            _logger.LogDebug("Final status - Mode: {Mode}, IsConnected: {IsConnected}, SSID: {SSID}", 
                status.CurrentMode, status.IsConnected, status.ConnectedNetworkSSID);

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
            var result = await ExecuteNmcliCommand(BuildWiFiListQuery());
            if (result.Success)
            {
                return ParseSignalStrengthFromWiFiList(result.Output, ssid);
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
            var result = await ExecuteNmcliCommand(BuildDeviceStatusQuery());
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

    // ===========================================================================================
    // UTILITY METHODS - NMCLI AND PARSING
    // ===========================================================================================

    private string BuildAPConnectionCommand(string wifiInterface, string ssid, string password)
    {
        return $"con add type wifi ifname {wifiInterface} con-name \"{ssid}\" autoconnect yes ssid \"{ssid}\" " +
               $"802-11-wireless.mode ap 802-11-wireless.band bg " +
               $"wifi-sec.key-mgmt wpa-psk wifi-sec.psk \"{password}\" " +
               $"ipv4.method shared ipv4.addresses {AP_IP_ADDRESS}/{AP_SUBNET} " +
               $"connection.autoconnect-priority 100";
    }

    private string BuildWiFiConnectCommand(string ssid, string password)
    {
        return $"device wifi connect \"{ssid}\" password \"{password}\"";
    }

    private string BuildConnectionDeleteCommand(string connectionName)
    {
        return $"con delete \"{connectionName}\"";
    }

    private string BuildConnectionUpCommand(string connectionName)
    {
        return $"con up \"{connectionName}\"";
    }

    private string BuildDeviceStatusQuery()
    {
        return "device status";
    }

    private string BuildWiFiListQuery()
    {
        return "device wifi list";
    }

    private (string deviceName, string deviceType, string state, string connectionName) ParseDeviceStatusLine(string line)
    {
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4)
        {
            return (parts[0], parts[1], parts[2], parts[3]);
        }
        return (string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private bool IsWiFiConnectedLine(string line)
    {
        return line.Contains("wifi") && (line.Contains("connected") || line.Contains("connecting"));
    }

    private int? ParseSignalStrengthFromWiFiList(string output, string ssid)
    {
        var lines = output.Split('\n');
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
        return null;
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

    // ===========================================================================================
    // PUBLIC API METHODS
    // ===========================================================================================

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
            _configManager.SaveConfiguration();

            // Switch modes immediately based on preference
            if (preferredMode == WiFiMode.AP)
            {
                // Only configure AP mode if not already in AP mode
                if (_currentStatus.CurrentMode != WiFiMode.AP || !_currentStatus.IsConnected)
                {
                    _logger.LogInformation("Preferred mode changed to AP, switching to AP mode immediately");
                    _isAttemptingConnection = false; // Stop any client connection attempts
                    var apPassword = _configManager.WiFiConfiguration.APSettings.Password;
                    await ConfigureAPMode(_configManager.APName, apPassword);
                }
            }
            else if (preferredMode == WiFiMode.Client)
            {
                // Only attempt client connection if not already connected in client mode
                if (_currentStatus.CurrentMode != WiFiMode.Client || !_currentStatus.IsConnected)
                {
                    _logger.LogInformation("Preferred mode changed to Client, attempting to connect to known networks");
                    await AttemptKnownNetworkConnection();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set preferred WiFi mode to: {Mode}", preferredMode);
            return false;
        }
    }

    public async Task<bool> SetAPPassword(string password)
    {
        try
        {
            _logger.LogInformation("Setting AP password");
            var apSettings = _configManager.WiFiConfiguration.APSettings;
            apSettings.Password = password;
            _configManager.SaveConfiguration();

            // Reconfigure AP with new password
            await ConfigureAPMode(_configManager.APName, password);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set AP password");
            return false;
        }
    }

    public WiFiAPConfiguration GetAPConfiguration()
    {
        var apSettings = _configManager.WiFiConfiguration.APSettings;
        return new WiFiAPConfiguration
        {
            SSID = _configManager.APName,
            Password = apSettings.Password,
            IPAddress = AP_IP_ADDRESS,
            Subnet = AP_SUBNET
        };
    }
}