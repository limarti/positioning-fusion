namespace Backend.Hardware.Gnss;

/// <summary>
/// UBX protocol constants tailored for u-blox ZED-X20P.
/// Focuses on modern CFG-VALSET/GET/DEL and messages the X20P actually supports.
/// </summary>
public static class UbxConstants
{
    // -------------------------
    // UBX protocol sync bytes
    // -------------------------
    public const byte SYNC_CHAR_1 = 0xB5;
    public const byte SYNC_CHAR_2 = 0x62;

    // -------------------------
    // Message classes
    // -------------------------
    public const byte CLASS_NAV = 0x01;  // Navigation results
    public const byte CLASS_RXM = 0x02;  // Receiver manager (RAWX/SFRBX etc.)
    public const byte CLASS_ACK = 0x05;  // ACK-ACK / ACK-NAK
    public const byte CLASS_CFG = 0x06;  // Configuration (VALSET/VALGET/VALDEL)
    public const byte CLASS_MON = 0x0A;  // Monitoring (MON-VER, etc.)
    public const byte CLASS_TIM = 0x0D;  // Timing messages

    // -------------------------
    // ACK messages
    // -------------------------
    public const byte ACK_NAK = 0x00;    // Not acknowledged
    public const byte ACK_ACK = 0x01;    // Acknowledged

    // -------------------------
    // MON messages
    // -------------------------
    public const byte MON_VER = 0x04;    // Receiver / software version
    public const byte MON_COMMS = 0x36;  // Communication port information

    // -------------------------
    // NAV messages (use modern, supported ones)
    // -------------------------
    public const byte NAV_PVT = 0x07;    // Position/Velocity/Time (primary)
    public const byte NAV_SAT = 0x35;    // Satellite info (per-satellite SV state)
    public const byte NAV_SIG = 0x43;    // Signal information (per-signal data)
    // NOTE: Avoid legacy NAV messages on X20P:
    // - NAV-STATUS (0x03) is deprecated on newer platforms.
    // - NAV-POSLLH (0x02) and NAV-SOL (0x06) are legacy; prefer NAV-PVT.

    // -------------------------
    // RXM messages
    // -------------------------
    public const byte RXM_SFRBX = 0x13;  // Broadcast nav data subframe (multi-GNSS)
    public const byte RXM_RAWX = 0x15;   // Raw measurement data
    public const byte RXM_COR = 0x34;    // Differential correction input status

    // -------------------------
    // TIM messages
    // -------------------------
    public const byte TIM_TM2 = 0x03;    // Time mark data
    public const byte TIM_TP = 0x01;     // Time pulse timedata

    // -------------------------
    // CFG messages
    // -------------------------
    // Modern key/value API (use these):
    public const byte CFG_VALSET = 0x8A; // Write configuration key/values
    public const byte CFG_VALGET = 0x8B; // Read configuration key/values
    public const byte CFG_VALDEL = 0x8C; // Delete configuration key/values
    public const byte CFG_RST = 0x04;    // Reset receiver / clear configuration

    // Legacy messages (avoid on X20P where possible; kept for completeness):
    public const byte CFG_PRT = 0x00;    // Port configuration (legacy)
    public const byte CFG_MSG = 0x01;    // Message rate config (legacy, per-port)

    // -------------------------
    // CFG-VAL* helpers
    // -------------------------
    // Version for VALSET/GET/DEL
    public const byte VAL_VERSION = 0x01;

    // Layers bitmask for VALSET/DEL (choose RAM unless you know you need persistence)
    public const byte VAL_LAYER_RAM = 0x01; // Volatile, applies immediately
    public const byte VAL_LAYER_BBR = 0x02; // Battery-backed RAM (if supported)
    public const byte VAL_LAYER_FLASH = 0x04; // Non-volatile (use cautiously)

    // Transaction modes for VALSET (0x06 0x8A)
    public enum ValTransaction : byte
    {
        None = 0x00, // Single message, no transaction
        New = 0x01, // Start new transaction
        Continue = 0x02, // Continue existing transaction
        Commit = 0x03  // Commit transaction
    }

    // -------------------------
    // Port indices (legacy CFG-MSG payload ordering � avoid for X20P)
    // -------------------------
    public const int PORT_INDEX_DDC = 0; // I2C
    public const int PORT_INDEX_UART1 = 1; // Primary UART
    public const int PORT_INDEX_UART2 = 2; // Secondary UART
    public const int PORT_INDEX_USB = 3; // USB
    public const int PORT_INDEX_SPI = 4; // SPI

    // -------------------------
    // Message output rates (generic)
    // -------------------------
    public const byte RATE_DISABLED = 0x00;
    public const byte RATE_1HZ = 0x01;
    public const byte RATE_2HZ = 0x02;
    public const byte RATE_5HZ = 0x05;

    // -------------------------
    // GNSS system IDs (for parsing NAV-SAT etc.)
    // -------------------------
    public const byte GNSS_ID_GPS = 0;
    public const byte GNSS_ID_SBAS = 1;
    public const byte GNSS_ID_GALILEO = 2;
    public const byte GNSS_ID_BEIDOU = 3;
    public const byte GNSS_ID_IMES = 4;
    public const byte GNSS_ID_QZSS = 5;
    public const byte GNSS_ID_GLONASS = 6;

    // -------------------------
    // Fix types (from NAV-PVT)
    // -------------------------
    public const byte FIX_TYPE_NO_FIX = 0;
    public const byte FIX_TYPE_DEAD_RECKONING = 1;
    public const byte FIX_TYPE_2D = 2;
    public const byte FIX_TYPE_3D = 3;
    public const byte FIX_TYPE_GNSS_DR = 4;
    public const byte FIX_TYPE_TIME_ONLY = 5;

    // -------------------------
    // Carrier solution (from NAV-PVT)
    // -------------------------
    public const byte CARRIER_SOLUTION_NONE = 0;
    public const byte CARRIER_SOLUTION_FLOAT = 1;
    public const byte CARRIER_SOLUTION_FIXED = 2;

    // -------------------------
    // NMEA message identifiers
    // -------------------------
    public const string NMEA_GGA = "GGA";  // Global Positioning System Fix Data
    public const string NMEA_RMC = "RMC";  // Recommended Minimum Specific GNSS Data
    public const string NMEA_GSV = "GSV";  // GNSS Satellites in View
    public const string NMEA_GSA = "GSA";  // GNSS DOP and Active Satellites
    public const string NMEA_VTG = "VTG";  // Track Made Good and Ground Speed
    public const string NMEA_GLL = "GLL";  // Geographic Position - Latitude/Longitude
    public const string NMEA_ZDA = "ZDA";  // Time & Date

    // -------------------------
    // CFG-VALSET message output key IDs
    // -------------------------
    public const uint MSGOUT_UBX_NAV_PVT_UART1 = 0x20910007;     // NAV-PVT UART1 output rate
    public const uint MSGOUT_UBX_NAV_SAT_UART1 = 0x20910016;     // NAV-SAT UART1 output rate
    public const uint MSGOUT_UBX_NAV_SIG_UART1 = 0x20910345;     // NAV-SIG UART1 output rate
    public const uint MSGOUT_UBX_RXM_RAWX_UART1 = 0x209102a5;    // RXM-RAWX UART1 output rate
    public const uint MSGOUT_UBX_RXM_SFRBX_UART1 = 0x20910232;   // RXM-SFRBX UART1 output rate
    public const uint MSGOUT_UBX_RXM_COR_UART1 = 0x209102b6;     // RXM-COR UART1 output rate
    public const uint MSGOUT_UBX_TIM_TM2_UART1 = 0x20910179;     // TIM-TM2 UART1 output rate
    public const uint MSGOUT_UBX_TIM_TP_UART1 = 0x2091017a;      // TIM-TP UART1 output rate
    public const uint MSGOUT_UBX_MON_COMMS_UART1 = 0x2091034f;   // MON-COMMS UART1 output rate

    // -------------------------
    // Recommended defaults for X20P UART
    // -------------------------
    // According to X20P defaults: UART1 is typically 38400 8N1,
    // UBX input and NMEA output enabled by default. You can switch
    // to higher rates via CFG-VALSET (UART1_BAUDRATE) then reopen.
    public const int DEFAULT_UART_BAUD = 38400;

}
