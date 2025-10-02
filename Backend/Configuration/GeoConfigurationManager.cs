using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Backend.WiFi;

namespace Backend.Configuration;

public enum OperatingMode
{
    DISABLED,
    RECEIVE,
    SEND
}

public class GeoConfigurationManager
{
    private readonly string _configFilePath;
    private AppConfiguration _configuration;
    private readonly ILogger<GeoConfigurationManager>? _logger;

    // Event for real-time mode change notifications
    public event EventHandler<OperatingMode>? OperatingModeChanged;

    public GeoConfigurationManager()
    {
        _configFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "system-config.json");
        _configuration = LoadConfiguration();
    }

    public GeoConfigurationManager(ILogger<GeoConfigurationManager> logger)
    {
        _logger = logger;
        _configFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "system-config.json");

        _logger.LogInformation("GeoConfigurationManager starting - Configuration file path: {ConfigPath}", _configFilePath);
        _logger.LogInformation("File exists check: {FileExists}", File.Exists(_configFilePath));

        _configuration = LoadConfiguration();

        _logger.LogInformation("GeoConfigurationManager initialized - OperatingMode: {OperatingMode}, WiFi PreferredMode: {WiFiPreferredMode}, Device Name: {DeviceName}",
            _configuration.OperatingMode, _configuration.WiFiConfiguration.PreferredMode, _configuration.DeviceName);
    }

    public OperatingMode OperatingMode
    {
        get => _configuration.OperatingMode;
        set
        {
            var oldMode = _configuration.OperatingMode;
            if (oldMode != value)
            {
                _logger?.LogDebug("Operating mode property changing from {OldMode} to {NewMode}", oldMode, value);
                _configuration.OperatingMode = value;
                _logger?.LogInformation("Operating mode property updated: {OldMode} → {NewMode}", oldMode, value);

                // Fire event to notify subscribers of mode change
                try
                {
                    OperatingModeChanged?.Invoke(this, value);
                    _logger?.LogDebug("OperatingModeChanged event fired for new mode: {NewMode}", value);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error firing OperatingModeChanged event for mode: {NewMode}", value);
                }
            }
            else
            {
                _logger?.LogDebug("Operating mode property set to same value: {Mode}", value);
            }
        }
    }

    public string DeviceName
    {
        get => _configuration.DeviceName;
        set
        {
            var oldName = _configuration.DeviceName;
            if (oldName != value)
            {
                _logger?.LogDebug("Device name property changing from {OldName} to {NewName}", oldName, value);
                _configuration.DeviceName = value;
                _logger?.LogInformation("Device name property updated: {OldName} → {NewName}", oldName, value);
            }
            else
            {
                _logger?.LogDebug("Device name property set to same value: {Name}", value);
            }
        }
    }

    public string APName => DeviceName;

    public string BluetoothName => DeviceName;

    public WiFiConfiguration WiFiConfiguration
    {
        get => _configuration.WiFiConfiguration;
        set
        {
            _logger?.LogDebug("WiFi configuration being updated");
            _configuration.WiFiConfiguration = value;
            _logger?.LogInformation("WiFi configuration updated");
        }
    }

    public void SaveConfiguration()
    {
        try
        {
            _logger?.LogInformation("Saving configuration");
            SaveConfigurationInternal();
            _logger?.LogInformation("Configuration saved successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save configuration");
            throw;
        }
    }

    private void SaveConfigurationInternal()
    {
        _logger?.LogDebug("Serializing configuration to JSON");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(_configuration, options);

        _logger?.LogDebug("Writing configuration to file: {ConfigPath}", _configFilePath);
        File.WriteAllText(_configFilePath, json);
    }

    private AppConfiguration LoadConfiguration()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                _logger?.LogInformation("Loading existing configuration from {ConfigPath}", _configFilePath);
                var json = File.ReadAllText(_configFilePath);
                _logger?.LogDebug("Raw configuration JSON: {Json}", json);

                var options = new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter() }
                };
                var config = JsonSerializer.Deserialize<AppConfiguration>(json, options);

                if (config?.WiFiConfiguration != null)
                {
                    _logger?.LogInformation("Loaded WiFi PreferredMode from file: {PreferredMode}", config.WiFiConfiguration.PreferredMode);
                }

                if (config != null)
                {
                    _logger?.LogInformation("Configuration loaded successfully from file with mode: {Mode}", config.OperatingMode);
                    return config;
                }
                else
                {
                    _logger?.LogWarning("Deserialized configuration was null, using default configuration");
                }
            }
            else
            {
                _logger?.LogInformation("Configuration file not found, using default configuration");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading configuration from {ConfigPath}, using default configuration", _configFilePath);
        }

        var defaultConfig = new AppConfiguration();
        _logger?.LogInformation("Using default configuration with mode: {Mode}", defaultConfig.OperatingMode);
        return defaultConfig;
    }

    private class AppConfiguration
    {
        public string DeviceName { get; set; } = GetDefaultDeviceName();
        public OperatingMode OperatingMode { get; set; } = OperatingMode.DISABLED;
        public WiFiConfiguration WiFiConfiguration { get; set; } = new();

        private static string GetDefaultDeviceName()
        {
            try
            {
                if (File.Exists("/etc/hostname"))
                {
                    var hostname = File.ReadAllText("/etc/hostname").Trim();
                    if (!string.IsNullOrEmpty(hostname))
                    {
                        return hostname;
                    }
                }
                return Environment.MachineName;
            }
            catch
            {
                return "raspberrypi";
            }
        }
    }
}

public class WiFiConfiguration
{
    public WiFiAPSettings APSettings { get; set; } = new();
    public List<StoredWiFiNetwork> KnownNetworks { get; set; } = new();
    public WiFiMode PreferredMode { get; set; } = WiFiMode.Client;
}

public class WiFiAPSettings
{
    public string Password { get; set; } = "subterra";
}

public class StoredWiFiNetwork
{
    public string SSID { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public DateTime LastConnected { get; set; }
}