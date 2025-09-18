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

    public static CorrectionsMode CorrectionsOperation { get; set; } = CorrectionsMode.Disabled;
    public static int GnssDataRate { get; set; } = 10; //1, 5 o 10 Hz
    
    // Bluetooth NMEA output rate for mobile apps (SW Maps, etc.)
    public static int BluetoothDataRate { get; set; } = 1; // Hz - typically 1-5 Hz for mobile apps
    
    // Bluetooth streaming configuration
    public static bool BluetoothStreamingEnabled { get; set; } = true;
    public static List<(byte messageClass, byte messageId)> BluetoothMessageFilter { get; set; } = new()
    {
        (UbxConstants.CLASS_NAV, UbxConstants.NAV_PVT), // NAV-PVT (Position Velocity Time)
        (UbxConstants.CLASS_NAV, UbxConstants.NAV_SAT)  // NAV-SAT (Satellite Information)
    };

    // Logging configuration
    public static int LoggingFlushIntervalSeconds { get; set; } = 10;
    public static int LoggingMaxBufferSizeBytes { get; set; } = 1048576; // 1MB
}