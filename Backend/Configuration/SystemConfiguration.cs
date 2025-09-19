using Backend.Hardware.Gnss;

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

    public static CorrectionsMode CorrectionsOperation { get; set; } = CorrectionsMode.Receive;

    // Logging configuration
    public static int LoggingFlushIntervalSeconds { get; set; } = 10;
    public static int LoggingMaxBufferSizeBytes { get; set; } = 1048576; // 1MB
}