using System.Reflection;

namespace Backend.Hardware.Gnss.Parsers;

public static class GnssParserUtils
{
    private static readonly Dictionary<byte, string> _constantNameCache = new();
    private static readonly Dictionary<uint, string> _keyIdNameCache = new();
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
        if (_constantNameCache.TryGetValue(value, out var cachedName))
        {
            return cachedName;
        }

        var fields = typeof(UbxConstants).GetFields(BindingFlags.Public | BindingFlags.Static);
        
        foreach (var field in fields)
        {
            if (field.FieldType == typeof(byte) && field.GetValue(null) is byte fieldValue && fieldValue == value)
            {
                var name = field.Name;
                _constantNameCache[value] = name;
                return name;
            }
        }
        
        var fallbackName = $"0x{value:X2}";
        _constantNameCache[value] = fallbackName;
        return fallbackName;
    }

    public static string GetKeyIdConstantName(uint keyId)
    {
        if (_keyIdNameCache.TryGetValue(keyId, out var cachedName))
        {
            return cachedName;
        }

        var fields = typeof(UbxConstants).GetFields(BindingFlags.Public | BindingFlags.Static);
        
        foreach (var field in fields)
        {
            if (field.FieldType == typeof(uint) && field.GetValue(null) is uint fieldValue && fieldValue == keyId)
            {
                var name = field.Name;
                _keyIdNameCache[keyId] = name;
                return name;
            }
        }
        
        var fallbackName = $"0x{keyId:X8}";
        _keyIdNameCache[keyId] = fallbackName;
        return fallbackName;
    }

}