using System.Text.Json;

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

    public GeoConfigurationManager()
    {
        _configFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "system-config.json");
        _configuration = LoadConfiguration();
    }

    public OperatingMode OperatingMode
    {
        get => _configuration.OperatingMode;
        set => _configuration.OperatingMode = value;
    }

    public void SaveConfiguration()
    {
        var json = JsonSerializer.Serialize(_configuration, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configFilePath, json);
    }

    private AppConfiguration LoadConfiguration()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<AppConfiguration>(json);
                return config ?? new AppConfiguration();
            }
        }
        catch (Exception)
        {
            // If there's any error reading the config, return default configuration
        }

        return new AppConfiguration();
    }

    private class AppConfiguration
    {
        public OperatingMode OperatingMode { get; set; } = OperatingMode.Disabled;
    }
}