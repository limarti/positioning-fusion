namespace Backend.GnssSystem;

public class SystemHealthUpdate
{
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public double Temperature { get; set; }
    public double BatteryLevel { get; set; }
    public double BatteryVoltage { get; set; }
    public bool IsExternalPowerConnected { get; set; }
}