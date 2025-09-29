using Backend.Hubs;
using Backend.Configuration;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class HighPrecisionPositionParser
{
    private static DateTime _lastSentTime = DateTime.MinValue;

    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        logger.LogDebug("ProcessNavHpPosLlh: Received {DataLength} bytes", data.Length);

        if (data.Length < 36)
        {
            logger.LogWarning("NAV-HPPOSLLH message too short: {Length} bytes, minimum 36 required", data.Length);
            return;
        }

        // Parse NAV-HPPOSLLH message payload
        var version = data[0];
        var reserved1 = BitConverter.ToUInt16(data, 1); // reserved bytes
        var reserved2 = data[3];
        var iTOW = BitConverter.ToUInt32(data, 4);
        
        // Main position components (1e-7 degrees, same as NAV-PVT)
        var lon = BitConverter.ToInt32(data, 8);
        var lat = BitConverter.ToInt32(data, 12);
        var height = BitConverter.ToInt32(data, 16); // height above ellipsoid (mm)
        var hMSL = BitConverter.ToInt32(data, 20); // height above MSL (mm)
        
        // High precision components (1e-9 degrees, 0.1mm)
        var lonHp = (sbyte)data[24];
        var latHp = (sbyte)data[25];
        var heightHp = (sbyte)data[26];
        var hMSLHp = (sbyte)data[27];
        
        // Accuracy estimates (0.1mm)
        var hAcc = BitConverter.ToUInt32(data, 28);
        var vAcc = BitConverter.ToUInt32(data, 32);

        // Combine main and high precision components for maximum precision
        // longitude = lon * 1e-7 + lonHp * 1e-9 (degrees)
        // latitude = lat * 1e-7 + latHp * 1e-9 (degrees)
        var longitudeDeg = lon * 1e-7 + lonHp * 1e-9;
        var latitudeDeg = lat * 1e-7 + latHp * 1e-9;
        
        // Height: combine main (mm) + high precision (0.1mm)
        var heightMeters = (height + heightHp * 0.1) / 1000.0; // Convert to meters
        var hMSLMeters = (hMSL + hMSLHp * 0.1) / 1000.0; // Convert to meters
        
        // Convert accuracy from 0.1mm to meters
        var hAccMeters = hAcc * 0.0001;
        var vAccMeters = vAcc * 0.0001;

        try
        {
            // Update the unified high-precision position store
            GnssService.UpdateHighPrecisionPosition(latitudeDeg, longitudeDeg, hMSLMeters);
            
            logger.LogDebug("📍 High precision position stored: Lat = {Lat:F9}°, Lon = {Lon:F9}°, HAcc = {HAcc:F4}m",
                latitudeDeg, longitudeDeg, hAccMeters);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to store high precision position data");
        }
    }
}