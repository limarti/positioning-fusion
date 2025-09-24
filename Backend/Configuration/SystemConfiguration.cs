namespace Backend.Configuration;

/// <summary>
/// System-wide configuration constants for the GNSS data collection system
/// </summary>
public static class SystemConfiguration
{
    public static int GnssDataRateDashboard { get; set; } = 2;  //Hz

    // Logging configuration
    public static int LoggingFlushIntervalSeconds { get; set; } = 10;
    public static int LoggingMaxBufferSizeBytes { get; set; } = 10 * 1024 * 1024;
}