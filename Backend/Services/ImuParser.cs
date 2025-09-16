using System.Text;

namespace Backend.Services;

public class ImuParser
{
    private readonly ILogger<ImuParser> _logger;
    private const int MEMS_PACKET_SIZE = 52;
    private const string HEADER = "fmi";
    private const char MEMS_TYPE = 'm';
    private const string TAIL = "ed";

    public ImuParser(ILogger<ImuParser> logger)
    {
        _logger = logger;
    }

    public ImuData? ParseMemsPacket(byte[] data)
    {
        if (data.Length != MEMS_PACKET_SIZE)
        {
            _logger.LogWarning("Invalid MEMS packet size: {Size}, expected {Expected}", data.Length, MEMS_PACKET_SIZE);
            return null;
        }

        try
        {
            var reader = new BinaryReader(new MemoryStream(data));

            // Read and validate header "fmi"
            string header = Encoding.ASCII.GetString(reader.ReadBytes(3));
            if (header != HEADER)
            {
                _logger.LogWarning("Invalid header: {Header}, expected {Expected}", header, HEADER);
                return null;
            }

            // Read and validate type byte 'm'
            char type = (char)reader.ReadByte();
            if (type != MEMS_TYPE)
            {
                _logger.LogWarning("Invalid type: {Type}, expected {Expected}", type, MEMS_TYPE);
                return null;
            }

            // Read payload (44 bytes): time (8) + 9 floats (36)
            double timestamp = reader.ReadDouble();
            
            float ax = reader.ReadSingle();
            float ay = reader.ReadSingle();
            float az = reader.ReadSingle();
            
            float gx = reader.ReadSingle();
            float gy = reader.ReadSingle();
            float gz = reader.ReadSingle();
            
            float mx = reader.ReadSingle();
            float my = reader.ReadSingle();
            float mz = reader.ReadSingle();

            // Read checksum
            ushort checksum = reader.ReadUInt16();

            // Read and validate tail "ed"
            string tail = Encoding.ASCII.GetString(reader.ReadBytes(2));
            if (tail != TAIL)
            {
                _logger.LogWarning("Invalid tail: {Tail}, expected {Expected}", tail, TAIL);
                return null;
            }

            // Validate checksum (sum of all preceding bytes mod 65536)
            ushort calculatedChecksum = CalculateChecksum(data, 0, data.Length - 4); // Exclude checksum and tail
            if (checksum != calculatedChecksum)
            {
                _logger.LogWarning("Checksum mismatch: received {Received:X4}, calculated {Calculated:X4}", 
                    checksum, calculatedChecksum);
                return null;
            }

            return new ImuData
            {
                Timestamp = timestamp,
                Acceleration = new Vector3 { X = ax, Y = ay, Z = az },
                Gyroscope = new Vector3 { X = gx, Y = gy, Z = gz },
                Magnetometer = new Vector3 { X = mx, Y = my, Z = mz }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing MEMS packet");
            return null;
        }
    }

    private static ushort CalculateChecksum(byte[] data, int start, int length)
    {
        uint sum = 0;
        for (int i = start; i < start + length; i++)
        {
            sum += data[i];
        }
        return (ushort)(sum % 65536);
    }
}

public class ImuData
{
    public double Timestamp { get; set; }
    public Vector3 Acceleration { get; set; } = new();
    public Vector3 Gyroscope { get; set; } = new();
    public Vector3 Magnetometer { get; set; } = new();
}

public class Vector3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}