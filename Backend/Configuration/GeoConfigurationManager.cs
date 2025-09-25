using System.Text.Json;
using Microsoft.Extensions.Logging;

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

        _logger.LogDebug("Configuration file path: {ConfigPath}", _configFilePath);
        _configuration = LoadConfiguration();
        _logger.LogInformation("GeoConfigurationManager initialized with operating mode: {Mode}", _configuration.OperatingMode);
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

    public void SaveConfiguration()
    {
        try
        {
            _logger?.LogDebug("Serializing configuration to JSON");
            var json = JsonSerializer.Serialize(_configuration, new JsonSerializerOptions { WriteIndented = true });

            _logger?.LogDebug("Writing configuration to file: {ConfigPath}", _configFilePath);
            File.WriteAllText(_configFilePath, json);

            _logger?.LogInformation("Configuration saved successfully with mode: {Mode}", _configuration.OperatingMode);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save configuration to {ConfigPath}", _configFilePath);
            throw;
        }
    }

    private AppConfiguration LoadConfiguration()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                _logger?.LogDebug("Loading existing configuration from {ConfigPath}", _configFilePath);
                var json = File.ReadAllText(_configFilePath);

                var config = JsonSerializer.Deserialize<AppConfiguration>(json);

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
    }
}