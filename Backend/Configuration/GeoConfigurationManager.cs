using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Backend.WiFi;

namespace Backend.Configuration;

public enum OperatingMode
{
    Disabled,
    Receive,
    Send
}

public class GeoConfigurationManager
{
    private readonly string _configFilePath;
    private AppConfiguration _configuration;
    private readonly ILogger<GeoConfigurationManager>? _logger;

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
        _logger.LogInformation("GeoConfigurationManager initialized - OperatingMode: {OperatingMode}, WiFi PreferredMode: {WiFiPreferredMode}",
            _configuration.OperatingMode, _configuration.WiFiConfiguration.PreferredMode);
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
            }
            else
            {
                _logger?.LogDebug("Operating mode property set to same value: {Mode}", value);
            }
        }
    }

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

    public void SaveOperatingMode()
    {
        try
        {
            _logger?.LogInformation("Saving operating mode: {Mode}", _configuration.OperatingMode);
            SaveConfigurationInternal();
            _logger?.LogInformation("Operating mode saved successfully: {Mode}", _configuration.OperatingMode);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save operating mode configuration");
            throw;
        }
    }

    public void SaveWiFiConfiguration()
    {
        try
        {
            _logger?.LogInformation("Saving WiFi configuration with preferred mode: {PreferredMode}", _configuration.WiFiConfiguration.PreferredMode);
            SaveConfigurationInternal();
            _logger?.LogInformation("WiFi configuration saved successfully with preferred mode: {PreferredMode}", _configuration.WiFiConfiguration.PreferredMode);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save WiFi configuration");
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
        public OperatingMode OperatingMode { get; set; } = OperatingMode.Disabled;
        public WiFiConfiguration WiFiConfiguration { get; set; } = new();
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
    public string SSID { get; set; } = "Subterra-AP";
    public string Password { get; set; } = "subterra";
    public string IPAddress { get; set; } = "10.200.1.1";
    public string Subnet { get; set; } = "24";
}

public class StoredWiFiNetwork
{
    public string SSID { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public DateTime LastConnected { get; set; }
}