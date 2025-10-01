using System.Collections.Concurrent;
using System.Reflection;

namespace Backend.Hardware.Gnss.Parsers;

public static class GnssParserUtils
{
    private static readonly ConcurrentDictionary<byte, string> _constantNameCache = new();
    private static readonly ConcurrentDictionary<uint, string> _keyIdNameCache = new();
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


    public static string GetConstantName(byte value)
    {
        return _constantNameCache.GetOrAdd(value, val =>
        {
            var fields = typeof(UbxConstants).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (var field in fields)
            {
                if (field.FieldType == typeof(byte) && field.GetValue(null) is byte fieldValue && fieldValue == val)
                {
                    return field.Name;
                }
            }

            return $"0x{val:X2}";
        });
    }

    public static string GetKeyIdConstantName(uint keyId)
    {
        return _keyIdNameCache.GetOrAdd(keyId, key =>
        {
            var fields = typeof(UbxConstants).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (var field in fields)
            {
                if (field.FieldType == typeof(uint) && field.GetValue(null) is uint fieldValue && fieldValue == key)
                {
                    return field.Name;
                }
            }

            return $"0x{key:X8}";
        });
    }

}