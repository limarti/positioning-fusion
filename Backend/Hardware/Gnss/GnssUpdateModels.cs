namespace Backend.Hardware.Gnss;

public class SatelliteUpdate
{
    public uint ITow { get; set; }
    public byte NumSatellites { get; set; }
    public List<SatelliteInfo> Satellites { get; set; } = new();
    public bool Connected { get; set; }
}

public class SatelliteInfo
{
    public byte GnssId { get; set; }
    public string GnssName { get; set; } = string.Empty;
    public byte SvId { get; set; }
    public byte Cno { get; set; }
    public sbyte Elevation { get; set; }
    public short Azimuth { get; set; }
    public double PseudorangeResidual { get; set; }
    public uint QualityIndicator { get; set; }
    public bool SvUsed { get; set; }
    public uint Health { get; set; }
    public bool DifferentialCorrection { get; set; }
    public bool Smoothed { get; set; }
}

public class PvtUpdate
{
    public uint ITow { get; set; }
    public ushort Year { get; set; }
    public byte Month { get; set; }
    public byte Day { get; set; }
    public byte Hour { get; set; }
    public byte Minute { get; set; }
    public byte Second { get; set; }
    public byte TimeValid { get; set; }
    public uint TimeAccuracy { get; set; }
    public byte FixType { get; set; }
    public bool GnssFixOk { get; set; }
    public bool DifferentialSolution { get; set; }
    public byte NumSatellites { get; set; }
    public double Longitude { get; set; }
    public double Latitude { get; set; }
    public int HeightEllipsoid { get; set; }
    public int HeightMSL { get; set; }
    public double HorizontalAccuracy { get; set; }
    public double VerticalAccuracy { get; set; }
    public int CarrierSolution { get; set; }
}

public class BroadcastDataUpdate
{
    public byte GnssId { get; set; }
    public string GnssName { get; set; } = string.Empty;
    public byte SvId { get; set; }
    public byte FrequencyId { get; set; }
    public byte Channel { get; set; }
    public int MessageLength { get; set; }
    public DateTime Timestamp { get; set; }
}


public class VersionUpdate
{
    public string SoftwareVersion { get; set; } = string.Empty;
    public string HardwareVersion { get; set; } = string.Empty;
    public string ReceiverType { get; set; } = string.Empty;
}

public class DataRatesUpdate
{
    public double? KbpsGnssIn { get; set; }
    public double? KbpsGnssOut { get; set; }
    public double? KbpsLoRaIn { get; set; }
    public double? KbpsLoRaOut { get; set; }
}

public class MessageRatesUpdate
{
    public Dictionary<string, double> MessageRates { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class DopUpdate
{
    public uint ITow { get; set; }
    public double GeometricDop { get; set; }
    public double PositionDop { get; set; }
    public double TimeDop { get; set; }
    public double VerticalDop { get; set; }
    public double HorizontalDop { get; set; }
    public double NorthingDop { get; set; }
    public double EastingDop { get; set; }
}