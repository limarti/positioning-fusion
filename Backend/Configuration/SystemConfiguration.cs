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

    public static CorrectionsMode CorrectionsOperation { get; set; } = CorrectionsMode.Disabled;
    public static int GnssDataRate { get; set; } = 1; // 1Hz default
}