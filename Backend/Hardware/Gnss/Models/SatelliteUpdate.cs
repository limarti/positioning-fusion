namespace Backend.Hardware.Gnss.Models;

public class SatelliteUpdate
{
    public uint ITow { get; set; }
    public byte NumSatellites { get; set; }
    public List<SatelliteInfo> Satellites { get; set; } = new();
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
