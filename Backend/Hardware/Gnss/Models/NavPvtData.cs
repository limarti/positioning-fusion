namespace Backend.Hardware.Gnss.Models;

public class NavPvtData
{
    public DateTime ReceivedAt { get; set; }
    public bool DiffSoln { get; set; }
    public int CarrierSolution { get; set; }
    public string FixType { get; set; } = string.Empty;
    public byte FixTypeRaw { get; set; }
    public bool GnssFixOk { get; set; }
    public byte NumSatellites { get; set; }

    // Differential correction age in milliseconds (null when not available, 0xFFFF in protocol)
    public uint? DiffAge { get; set; }
}
