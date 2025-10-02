namespace Backend.Hardware.Gnss.Models;

public class VersionUpdate
{
    public string SoftwareVersion { get; set; } = string.Empty;
    public string HardwareVersion { get; set; } = string.Empty;
    public string ReceiverType { get; set; } = string.Empty;
}
