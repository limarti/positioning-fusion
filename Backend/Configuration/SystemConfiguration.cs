using Backend.Hardware.Gnss;
using System.Text.Json;

namespace Backend.Configuration;

/// <summary>
/// System-wide configuration constants for the GNSS data collection system
/// </summary>
public static class SystemConfiguration
{
    public enum CorrectionsMode
    {
        Disabled,   // No RTK corrections (standalone GNSS)
        Receive,    // Receive RTK corrections (rover mode)
        Send        // Send RTK corrections (base station mode)
    }

    public static int GnssDataRateDashboard { get; set; } = 2;  //Hz

    public static CorrectionsMode CorrectionsOperation { get; set; }

    // Logging configuration
    public static int LoggingFlushIntervalSeconds { get; set; } = 10;
    public static int LoggingMaxBufferSizeBytes { get; set; } = 1048576; // 1MB

    // Operating mode persistence
    private static readonly string ConfigFilePath = "rtk-mode.json";

    public static CorrectionsMode? LoadRtkMode()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<OperatingModeConfig>(json);
                if (config?.OperatingMode != null && Enum.TryParse<CorrectionsMode>(config.OperatingMode, out var mode))
                {
                    return mode;
                }
            }
        }
        catch (Exception)
        {
            // If there's any error reading the config, return null to trigger fresh selection
        }
        return null;
    }

    public static void SaveOperatingMode(CorrectionsMode operatingMode)
    {
        var config = new OperatingModeConfig { OperatingMode = operatingMode.ToString() };
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigFilePath, json);
    }



    private class OperatingModeConfig
    {
        public string? OperatingMode { get; set; }
    }
}