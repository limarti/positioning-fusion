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

    // NMEA sentence subscription configuration
    public static Dictionary<string, int> NmeaSentenceRates { get; set; } = new()
    {
        { UbxConstants.NMEA_GGA, 1 },  // Global Positioning System Fix Data - 1 Hz
        { UbxConstants.NMEA_RMC, 1 },  // Recommended Minimum - 1 Hz
        { UbxConstants.NMEA_GSV, 1 },  // GNSS Satellites in View - 1 Hz
        { UbxConstants.NMEA_GSA, 1 },  // GNSS DOP and Active Satellites - 1 Hz
        { UbxConstants.NMEA_VTG, 1 },  // Track Made Good and Ground Speed - 1 Hz
        { UbxConstants.NMEA_GLL, 0 },  // Geographic Position - Latitude/Longitude - Disabled
        { UbxConstants.NMEA_ZDA, 0 }   // Time & Date - Disabled
    };

    // Logging configuration
    public static int LoggingFlushIntervalSeconds { get; set; } = 10;
    public static int LoggingMaxBufferSizeBytes { get; set; } = 1048576; // 1MB
}