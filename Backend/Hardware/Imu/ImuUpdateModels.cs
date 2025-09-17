namespace Backend.Hardware.Imu;

public class ImuUpdate
{
    public double Timestamp { get; set; }
    public Vector3Update Acceleration { get; set; } = new();
    public Vector3Update Gyroscope { get; set; } = new();
    public Vector3Update Magnetometer { get; set; } = new();
}

public class Vector3Update
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}