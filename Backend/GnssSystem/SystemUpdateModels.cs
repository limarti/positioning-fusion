namespace Backend.GnssSystem;

public class SystemHealthUpdate
{
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public double Temperature { get; set; }
    public double BatteryLevel { get; set; }
    public double BatteryVoltage { get; set; }
    public bool IsExternalPowerConnected { get; set; }
    public string Hostname { get; set; } = string.Empty;
}

public class CorrectionsStatusUpdate
{
    public string Mode { get; set; } = "Disabled"; // "Disabled", "Receive", "Send"
}

public class HostnameUpdateRequest
{
    public string Hostname { get; set; } = string.Empty;
}

public class HostnameUpdateResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string CurrentHostname { get; set; } = string.Empty;
}