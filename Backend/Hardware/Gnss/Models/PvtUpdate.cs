namespace Backend.Hardware.Gnss.Models;

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
    public long GnssTimestamp { get; set; }
    public byte FixType { get; set; }
    public string FixTypeString { get; set; } = string.Empty;
    public bool GnssFixOk { get; set; }
    public bool DifferentialSolution { get; set; }
    public byte NumSatellites { get; set; }

    // Coordinates from NAV-PVT (7 decimal places, ~11mm precision)
    // Use HpPositionUpdate for higher precision (11 decimal places, ~0.01mm precision) when not in base mode
    public double Longitude { get; set; }
    public double Latitude { get; set; }
    public int HeightEllipsoid { get; set; }
    public int HeightMSL { get; set; }

    public double HorizontalAccuracy { get; set; }
    public double VerticalAccuracy { get; set; }
    public int CarrierSolution { get; set; }

    // Differential correction age in milliseconds (null when not available, 0xFFFF in protocol)
    public uint? DiffAge { get; set; }
}
