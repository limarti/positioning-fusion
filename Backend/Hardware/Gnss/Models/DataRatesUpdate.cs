namespace Backend.Hardware.Gnss.Models;

public class DataRatesUpdate
{
    public double? KbpsGnssIn { get; set; }
    public double? KbpsGnssOut { get; set; }
    public double? KbpsLoRaIn { get; set; }
    public double? KbpsLoRaOut { get; set; }
}
