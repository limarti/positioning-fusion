namespace Backend.Hardware.Camera;

public class CameraData
{
    public DateTime Timestamp { get; set; }
    public string ImageBase64 { get; set; } = string.Empty;
    public int ImageSizeBytes { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string Format { get; set; } = "JPEG";
}

public class CameraUpdate
{
    public DateTime Timestamp { get; set; }
    public string ImageBase64 { get; set; } = string.Empty;
    public int ImageSizeBytes { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string Format { get; set; } = "JPEG";
    public double CaptureTimeMs { get; set; }
    public double EncodingTimeMs { get; set; }
    public bool IsConnected { get; set; }
}