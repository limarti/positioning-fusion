namespace Backend.Hardware.Gnss.Models;

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
