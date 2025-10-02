namespace Backend.Hardware.Gnss.Models;

public class HpPositionUpdate
{
    public double Latitude { get; set; }         // High precision latitude (1e-11 degrees, ~0.01mm)
    public double Longitude { get; set; }        // High precision longitude (1e-11 degrees, ~0.01mm)
    public double HeightMSL { get; set; }        // Height above mean sea level (meters, 0.1mm precision)
    public double HorizontalAccuracy { get; set; } // Horizontal accuracy estimate (meters)
    public double VerticalAccuracy { get; set; }   // Vertical accuracy estimate (meters)
}
