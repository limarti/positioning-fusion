using System.ComponentModel.DataAnnotations;

namespace Backend.WiFi;

public class WiFiStatusUpdate
{
    public WiFiMode CurrentMode { get; set; }
    public string? ConnectedNetworkSSID { get; set; }
    public int? SignalStrength { get; set; }
    public bool IsConnected { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class WiFiFallbackNotification
{
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class WiFiKnownNetworksUpdate
{
    public List<KnownWiFiNetwork> Networks { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class KnownWiFiNetwork
{
    public string SSID { get; set; } = string.Empty;
    public DateTime LastConnected { get; set; }
}

public class WiFiConnectionRequest
{
    [Required]
    public string SSID { get; set; } = string.Empty;
    
    [Required]
    public string Password { get; set; } = string.Empty;
    
    public bool SaveToKnownNetworks { get; set; } = true;
}

public class WiFiAPConfigurationRequest
{
    [Required]
    public string SSID { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class WiFiModePreferenceRequest
{
    [Required]
    public WiFiMode PreferredMode { get; set; }
}

public class WiFiAPConfiguration
{
    public string SSID { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public string Subnet { get; set; } = string.Empty;
}

public enum WiFiMode
{
    AP,
    Client,
    Disconnected
}