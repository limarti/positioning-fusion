using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class Rtcm1005Parser
{
    public static async Task ProcessAsync(byte[] rtcmMessage, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        //logger.LogInformation("🔍 RTCM 1005 Parser: Processing message with {Length} bytes", rtcmMessage.Length);
        
        try
        {
            // RTCM3 message 1005 structure:
            // Header: 3 bytes (0xD3 + length)
            // Message type: 12 bits (1005)
            // Reference station ID: 12 bits
            // ITRF realization year: 6 bits
            // GPS indicator: 1 bit
            // GLONASS indicator: 1 bit
            // Galileo indicator: 1 bit
            // Reference station indicator: 1 bit
            // ECEF-X coordinate: 38 bits (0.0001 m resolution)
            // Single receiver oscillator: 1 bit
            // Reserved: 1 bit
            // ECEF-Y coordinate: 38 bits (0.0001 m resolution)
            // Quarter cycle indicator: 2 bits
            // ECEF-Z coordinate: 38 bits (0.0001 m resolution)
            // CRC: 24 bits

            if (rtcmMessage.Length < 22) // Minimum length for RTCM 1005
            {
                logger.LogWarning("RTCM 1005 message too short: {Length} bytes", rtcmMessage.Length);
                return;
            }

            // Skip header (3 bytes) and extract payload
            var payload = rtcmMessage.Skip(3).Take(rtcmMessage.Length - 6).ToArray(); // Remove header and CRC

            if (payload.Length < 19) // Minimum payload for RTCM 1005
            {
                logger.LogWarning("RTCM 1005 payload too short: {Length} bytes", payload.Length);
                return;
            }

            // Convert to bit array for easier bit manipulation
            var bits = new bool[payload.Length * 8];
            for (int i = 0; i < payload.Length; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    bits[i * 8 + j] = (payload[i] & (0x80 >> j)) != 0;
                }
            }

            // Parse fields (bit positions from RTCM 3.3 standard)
            int bitPos = 12; // Skip message type (first 12 bits)

            // Reference Station ID (12 bits)
            var stationId = ExtractBits(bits, bitPos, 12);
            bitPos += 12;

            // Skip ITRF year (6 bits), GPS/GLO/GAL indicators (3 bits), ref station indicator (1 bit)
            bitPos += 10;

            // ECEF-X coordinate (38 bits, signed, 0.0001 m resolution)
            var ecefX = ExtractSignedBits(bits, bitPos, 38) * 0.0001;
            bitPos += 38;

            // Skip single receiver oscillator (1 bit) and reserved (1 bit)
            bitPos += 2;

            // ECEF-Y coordinate (38 bits, signed, 0.0001 m resolution)
            var ecefY = ExtractSignedBits(bits, bitPos, 38) * 0.0001;
            bitPos += 38;

            // Skip quarter cycle indicator (2 bits)
            bitPos += 2;

            // ECEF-Z coordinate (38 bits, signed, 0.0001 m resolution)
            var ecefZ = ExtractSignedBits(bits, bitPos, 38) * 0.0001;

            logger.LogInformation("🔢 ECEF coordinates: X={X:F4}m, Y={Y:F4}m, Z={Z:F4}m", ecefX, ecefY, ecefZ);

            // Convert ECEF to Latitude/Longitude (WGS84)
            var (latitude, longitude, altitude) = EcefToGeodetic(ecefX, ecefY, ecefZ);

            logger.LogInformation("📍 Base Station Position: Lat={Lat:F8}°, Lon={Lon:F8}°, Alt={Alt:F3}m (Station ID: {StationId})", 
                latitude, longitude, altitude, stationId);

            // Send reference station position to frontend
            await hubContext.Clients.All.SendAsync("ReferenceStationPosition", new
            {
                StationId = stationId,
                Latitude = latitude,
                Longitude = longitude,
                Altitude = altitude,
                EcefX = ecefX,
                EcefY = ecefY,
                EcefZ = ecefZ,
                Timestamp = DateTime.UtcNow
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing RTCM 1005 message");
        }
    }

    private static uint ExtractBits(bool[] bits, int startBit, int numBits)
    {
        uint result = 0;
        for (int i = 0; i < numBits; i++)
        {
            if (startBit + i < bits.Length && bits[startBit + i])
            {
                result |= (uint)(1 << (numBits - 1 - i));
            }
        }
        return result;
    }

    private static long ExtractSignedBits(bool[] bits, int startBit, int numBits)
    {
        long result = 0;
        for (int i = 0; i < numBits; i++)
        {
            if (startBit + i < bits.Length && bits[startBit + i])
            {
                result |= (long)(1L << (numBits - 1 - i));
            }
        }

        // Handle two's complement for negative numbers
        if (numBits > 0 && startBit < bits.Length && bits[startBit])
        {
            result -= (1L << numBits);
        }

        return result;
    }

    private static (double latitude, double longitude, double altitude) EcefToGeodetic(double x, double y, double z)
    {
        // WGS84 constants
        const double a = 6378137.0;           // Semi-major axis (m)
        const double f = 1.0 / 298.257223563; // Flattening
        const double e2 = 2 * f - f * f;      // First eccentricity squared

        double longitude = Math.Atan2(y, x) * 180.0 / Math.PI;

        double p = Math.Sqrt(x * x + y * y);
        double latitude = Math.Atan2(z, p * (1 - e2));

        // Iterative calculation for latitude and altitude
        double prevLat;
        double altitude = 0;
        int iterations = 0;
        
        do
        {
            prevLat = latitude;
            double sinLat = Math.Sin(latitude);
            double N = a / Math.Sqrt(1 - e2 * sinLat * sinLat);
            altitude = p / Math.Cos(latitude) - N;
            latitude = Math.Atan2(z, p * (1 - e2 * N / (N + altitude)));
            iterations++;
        } while (Math.Abs(latitude - prevLat) > 1e-12 && iterations < 10);

        latitude *= 180.0 / Math.PI;

        return (latitude, longitude, altitude);
    }
}