namespace Backend.Hardware.Gnss.Models;

public class RelativePositionUpdate
{
    public uint ITow { get; set; }
    public double RelPosN { get; set; } // Relative position North (m)
    public double RelPosE { get; set; } // Relative position East (m)
    public double RelPosD { get; set; } // Relative position Down (m)
    public double RelPosLength { get; set; } // Distance to base station (m)
    public double RelPosHeading { get; set; } // Heading of vector to base (degrees)
    public double AccN { get; set; } // Accuracy North (m)
    public double AccE { get; set; } // Accuracy East (m)
    public double AccD { get; set; } // Accuracy Down (m)
    public double AccLength { get; set; } // Accuracy of distance (m)
    public double AccHeading { get; set; } // Accuracy of heading (degrees)
    public bool RelPosValid { get; set; } // Relative position is valid
    public bool RelPosNormalized { get; set; } // Components are normalized
    public byte CarrSoln { get; set; } // Carrier solution status
    public bool IsMoving { get; set; } // Receiver is moving
    public bool RefPosMiss { get; set; } // Reference position missing
    public bool RefObsMiss { get; set; } // Reference observations missing
    public bool RelPosHeadingValid { get; set; } // Relative position heading is valid
}
