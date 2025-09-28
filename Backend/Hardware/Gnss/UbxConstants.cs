namespace Backend.Hardware.Gnss
{
    /// <summary>
    /// UBX / NMEA / RTCM constants for u-blox ZED-X20P (CFG-VAL* oriented), UART1-focused.
    /// </summary>
    public static class UbxConstants
    {
        // --- UBX sync bytes
        public const byte SYNC_CHAR_1 = 0xB5;
        public const byte SYNC_CHAR_2 = 0x62;

        // --- RTCM sync
        public const byte RTCM3_PREAMBLE = 0xD3;
        public const int RTCM3_MIN_LENGTH = 6;

        // --- UBX classes
        public const byte CLASS_NAV = 0x01;   // NAV-*
        public const byte CLASS_RXM = 0x02;   // RXM-*
        public const byte CLASS_ACK = 0x05;   // ACK-ACK / ACK-NAK
        public const byte CLASS_CFG = 0x06;   // CFG-*
        public const byte CLASS_MON = 0x0A;   // MON-*
        public const byte CLASS_TIM = 0x0D;   // TIM-*

        // --- ACK ids
        public const byte ACK_NAK = 0x00;
        public const byte ACK_ACK = 0x01;

        // --- MON ids
        public const byte MON_VER = 0x04;
        public const byte MON_COMMS = 0x36;

        // --- NAV ids (modern)
        public const byte NAV_PVT = 0x07;
        public const byte NAV_DOP = 0x04;
        public const byte NAV_SAT = 0x35;
        public const byte NAV_SIG = 0x43;
        public const byte NAV_SVIN = 0x3B;
        public const byte NAV_RELPOSNED = 0x3C;

        // --- RXM ids
        public const byte RXM_SFRBX = 0x13;
        public const byte RXM_RAWX = 0x15;
        public const byte RXM_COR = 0x34;

        // --- TIM ids
        public const byte TIM_TM2 = 0x03;
        public const byte TIM_TP = 0x01;

        // --- CFG ids (modern + selected legacy)
        public const byte CFG_VALSET = 0x8A;
        public const byte CFG_VALGET = 0x8B;
        public const byte CFG_VALDEL = 0x8C;
        public const byte CFG_RST = 0x04; // reset
        public const byte CFG_PRT = 0x00; // legacy port cfg (avoid; prefer VALSET)
        public const byte CFG_MSG = 0x01; // legacy per-port rates (avoid)

        // --- CFG-VAL* header fields
        public const byte VAL_VERSION = 0x01;
        public const byte VAL_LAYER_RAM = 0x01;   // apply at runtime
        public const byte VAL_LAYER_BBR = 0x02;
        public const byte VAL_LAYER_FLASH = 0x04;

        public enum ValTransaction : byte { None = 0x00, New = 0x01, Continue = 0x02, Commit = 0x03 }

        // --- GNSS ids (for parsing)
        public const byte GNSS_ID_GPS = 0;
        public const byte GNSS_ID_SBAS = 1;
        public const byte GNSS_ID_GALILEO = 2;
        public const byte GNSS_ID_BEIDOU = 3;
        public const byte GNSS_ID_IMES = 4;
        public const byte GNSS_ID_QZSS = 5;
        public const byte GNSS_ID_GLONASS = 6;

        // --- Fix types (NAV-PVT)
        public const byte FIX_TYPE_NO_FIX = 0;
        public const byte FIX_TYPE_DEAD_RECKONING = 1;
        public const byte FIX_TYPE_2D = 2;
        public const byte FIX_TYPE_3D = 3;
        public const byte FIX_TYPE_GNSS_DR = 4;
        public const byte FIX_TYPE_TIME_ONLY = 5;

        // --- Carrier solution (NAV-PVT.carrSoln)
        public const byte CARRIER_SOLUTION_NONE = 0;
        public const byte CARRIER_SOLUTION_FLOAT = 1;
        public const byte CARRIER_SOLUTION_FIXED = 2;

        // --- NMEA tags
        public const string NMEA_GGA = "GGA";
        public const string NMEA_RMC = "RMC";
        public const string NMEA_GSV = "GSV";
        public const string NMEA_GSA = "GSA";
        public const string NMEA_VTG = "VTG";
        public const string NMEA_GLL = "GLL";
        public const string NMEA_ZDA = "ZDA";

        // --- Message output rate helpers (value byte written via VALSET)
        public const byte RATE_DISABLED = 0x00; // off
        public const byte RATE_1X = 0x01;       // every epoch
        // Higher divisors are 2, 3, ... (every Nth epoch).

        // --- Common config keys (CFG-VAL* keyIds)

        // UART1 protocol enables (OUT)
        public const uint UART1_PROTOCOL_UBX = 0x10740001;
        public const uint UART1_PROTOCOL_NMEA = 0x10740002;
        public const uint UART1_PROTOCOL_RTCM3 = 0x10740004;

        // UART1 protocol enables (IN)
        public const uint UART1_PROTOCOL_UBX_IN = 0x10730001;
        public const uint UART1_PROTOCOL_NMEA_IN = 0x10730002;
        public const uint UART1_PROTOCOL_RTCM3_IN = 0x10730004;

        // UART2 protocol enables (OUT)
        public const uint UART2_PROTOCOL_UBX = 0x10750001;
        public const uint UART2_PROTOCOL_NMEA = 0x10750002;
        public const uint UART2_PROTOCOL_RTCM3 = 0x10750004;

        // Navigation rate (ms per epoch)
        public const uint CFG_RATE_MEAS = 0x30210001; // U2 (100→10 Hz, 200→5 Hz, 1000→1 Hz)

        // Time mode / survey-in (values below)
        public const uint TMODE_MODE = 0x20030001;         // U1: 0=Disabled,1=Survey-In,2=Fixed
        public const uint TMODE_SVIN_MIN_DUR = 0x40030010; // U4: seconds
        public const uint TMODE_SVIN_ACC_LIMIT = 0x40030011; // U4: 0.1 mm units

        public const byte TMODE_DISABLED = 0;
        public const byte TMODE_SURVEY_IN = 1;
        public const byte TMODE_FIXED = 2;

        // Fast test defaults (adjust in app code if needed)
        public const uint SURVEY_IN_DURATION_SECONDS = 10;          // fast for testing
        public const uint SURVEY_IN_ACCURACY_LIMIT_0P1MM = 1_000_000; // 100.0 m = 1000000 * 0.1 mm

        // --- UBX message outputs (UART1) via CFG-MSGOUT-* key ids (value: U1 rate byte)
        public const uint MSGOUT_UBX_NAV_PVT_UART1 = 0x20910007;
        public const uint MSGOUT_UBX_NAV_SAT_UART1 = 0x20910016;
        public const uint MSGOUT_UBX_NAV_SIG_UART1 = 0x20910346; // FIXED: was 0x20910345 (I2C)
        public const uint MSGOUT_UBX_NAV_SVIN_UART1 = 0x20910089;
        public const uint MSGOUT_UBX_NAV_DOP_UART1 = 0x20910039;
        public const uint MSGOUT_UBX_NAV_RELPOSNED_UART1 = 0x2091008e;

        public const uint MSGOUT_UBX_RXM_RAWX_UART1 = 0x209102A5;
        public const uint MSGOUT_UBX_RXM_SFRBX_UART1 = 0x20910232;
        public const uint MSGOUT_UBX_RXM_COR_UART1 = 0x209102B6;

        public const uint MSGOUT_UBX_TIM_TM2_UART1 = 0x20910179;
        public const uint MSGOUT_UBX_TIM_TP_UART1 = 0x2091017A;

        public const uint MSGOUT_UBX_MON_COMMS_UART1 = 0x20910350; // FIXED: was 0x2091034F (I2C)

        // --- NMEA message outputs (UART1) via CFG-MSGOUT-* key ids (value: U1 rate byte)
        public const uint MSGOUT_NMEA_GGA_UART1 = 0x209100BB;
        public const uint MSGOUT_NMEA_RMC_UART1 = 0x209100AC;
        public const uint MSGOUT_NMEA_GSV_UART1 = 0x209100C9;
        public const uint MSGOUT_NMEA_GSA_UART1 = 0x209100BF;
        public const uint MSGOUT_NMEA_VTG_UART1 = 0x209100B1;
        public const uint MSGOUT_NMEA_GLL_UART1 = 0x209100C5;
        public const uint MSGOUT_NMEA_ZDA_UART1 = 0x209100D5;

        // --- RTCM 3.x outputs (UART1) via CFG-MSGOUT-* key ids (value: U1 rate byte)
        // Recommended base set on X20P: 1005 + 1074 + 1094 + 1124 (same rate for all observations).
        public const uint MSGOUT_RTCM3_REF_STATION_ARP_UART1 = 0x209102BE; // 1005

        public const uint MSGOUT_RTCM3_GPS_MSM4_UART1 = 0x2091035F; // 1074
        public const uint MSGOUT_RTCM3_GPS_MSM7_UART1 = 0x209102CD; // 1077

        public const uint MSGOUT_RTCM3_GALILEO_MSM4_UART1 = 0x20910369; // 1094
        public const uint MSGOUT_RTCM3_GALILEO_MSM7_UART1 = 0x20910319; // 1097

        public const uint MSGOUT_RTCM3_BEIDOU_MSM4_UART1 = 0x2091036E; // 1124
        public const uint MSGOUT_RTCM3_BEIDOU_MSM7_UART1 = 0x209102D7; // 1127

        // Optional / compatibility (use only if rover/stream requires them)
        public const uint MSGOUT_RTCM3_GLONASS_MSM4_UART1 = 0x20910364; // 1084
        public const uint MSGOUT_RTCM3_GLONASS_CODE_PHASE_BIASES_UART1 = 0x20910304; // 1230

        // --- Defaults
        public const int DEFAULT_UART_BAUD = 38400;
    }
}
