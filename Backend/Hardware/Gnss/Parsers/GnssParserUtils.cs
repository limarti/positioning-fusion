namespace Backend.Hardware.Gnss.Parsers;

public static class GnssParserUtils
{
    public static string GetGnssName(byte gnssId)
    {
        return gnssId switch
        {
            UbxConstants.GNSS_ID_GPS => "GPS",
            UbxConstants.GNSS_ID_SBAS => "SBAS",
            UbxConstants.GNSS_ID_GALILEO => "Galileo",
            UbxConstants.GNSS_ID_BEIDOU => "BeiDou",
            UbxConstants.GNSS_ID_IMES => "IMES",
            UbxConstants.GNSS_ID_QZSS => "QZSS",
            UbxConstants.GNSS_ID_GLONASS => "GLONASS",
            _ => $"Unknown({gnssId})"
        };
    }
}