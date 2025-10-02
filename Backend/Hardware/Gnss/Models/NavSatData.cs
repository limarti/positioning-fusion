namespace Backend.Hardware.Gnss.Models;

public class NavSatData
{
    public DateTime ReceivedAt { get; set; }
    public int TotalSatellites { get; set; }
    public int UsedSatellites { get; set; }
    public int DiffCorrSatellites { get; set; }
    public bool SbasInUse { get; set; }
    public bool DiffCorrInUse { get; set; }
}
