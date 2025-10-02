namespace Backend.Hardware.Gnss.Models;

public class MessageRatesUpdate
{
    public Dictionary<string, double> MessageRates { get; set; } = new();
    public DateTime Timestamp { get; set; }
}
