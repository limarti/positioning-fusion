namespace Backend.Hardware.Gnss.Models;

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
