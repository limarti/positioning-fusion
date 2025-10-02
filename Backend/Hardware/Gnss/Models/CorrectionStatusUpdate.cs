namespace Backend.Hardware.Gnss.Models;

public class CorrectionStatusUpdate
{
    public byte Version { get; set; }
    public ushort CorrectionFlags { get; set; }
    public ushort MessageType { get; set; }
    public ushort MessageSubType { get; set; }
    public ushort NumMessages { get; set; }
    public uint? CorrectionAge { get; set; } // Nullable - null when age is unknown
    public bool CorrectionValid { get; set; }
    public bool CorrectionStale { get; set; }
    public bool SbasCorrections { get; set; }
    public bool RtcmCorrections { get; set; }
    public bool SpartnCorrections { get; set; }
    public string CorrectionSource { get; set; } = string.Empty;
    public string CorrectionStatus { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
