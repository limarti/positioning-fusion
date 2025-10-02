using Backend.Hubs;
using Backend.Configuration;
using Backend.Hardware.Gnss.Models;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class RelativePositionParser
{
    private static DateTime _lastSentTime = DateTime.MinValue;

    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        logger.LogDebug("ProcessNavRelposned: Received {DataLength} bytes", data.Length);

        if (data.Length < 64)
        {
            logger.LogWarning("NAV-RELPOSNED message too short: {Length} bytes, minimum 64 required", data.Length);
            return;
        }

        // Parse NAV-RELPOSNED message payload
        var version = data[0];
        var reserved1 = data[1];
        var refStationId = BitConverter.ToUInt16(data, 2);
        var iTOW = BitConverter.ToUInt32(data, 4);
        
        // Relative position in NED frame (cm)
        var relPosN = BitConverter.ToInt32(data, 8);
        var relPosE = BitConverter.ToInt32(data, 12);
        var relPosD = BitConverter.ToInt32(data, 16);
        
        // High precision parts (0.1mm)
        var relPosHPN = (sbyte)data[20];
        var relPosHPE = (sbyte)data[21];
        var relPosHPD = (sbyte)data[22];
        var reserved2 = data[23];
        
        // Accuracy estimates (0.1mm)
        var accN = BitConverter.ToUInt32(data, 24);
        var accE = BitConverter.ToUInt32(data, 28);
        var accD = BitConverter.ToUInt32(data, 32);
        
        // Flags
        var flags = BitConverter.ToUInt32(data, 36);
        
        // Parse flags
        var gnssFixOK = (flags & 0x01) != 0;
        var diffSoln = (flags & 0x02) != 0;
        var relPosValid = (flags & 0x04) != 0;
        var carrSoln = (byte)((flags >> 3) & 0x03);
        var isMoving = (flags & 0x20) != 0;
        var refPosMiss = (flags & 0x40) != 0;
        var refObsMiss = (flags & 0x80) != 0;
        var relPosHeadingValid = (flags & 0x100) != 0;
        var relPosNormalized = (flags & 0x200) != 0;

        // Convert to meters with high precision
        var relPosNMeters = relPosN * 0.01 + relPosHPN * 0.0001;
        var relPosEMeters = relPosE * 0.01 + relPosHPE * 0.0001;
        var relPosD_meters = relPosD * 0.01 + relPosHPD * 0.0001;
        
        // Calculate distance to base station
        var relPosLength = Math.Sqrt(relPosNMeters * relPosNMeters + 
                                   relPosEMeters * relPosEMeters + 
                                   relPosD_meters * relPosD_meters);
        
        // Calculate heading to base (degrees from North)
        var relPosHeading = Math.Atan2(relPosEMeters, relPosNMeters) * 180.0 / Math.PI;
        if (relPosHeading < 0) relPosHeading += 360.0;
        
        // Convert accuracy to meters
        var accNMeters = accN * 0.0001;
        var accEMeters = accE * 0.0001;
        var accDMeters = accD * 0.0001;
        
        // Calculate accuracy of distance
        var accLength = Math.Sqrt(accNMeters * accNMeters + accEMeters * accEMeters + accDMeters * accDMeters);
        
        // Estimate heading accuracy (simplified approximation)
        var accHeading = relPosLength > 0.1 ? 
            Math.Atan2(Math.Sqrt(accNMeters * accNMeters + accEMeters * accEMeters), relPosLength) * 180.0 / Math.PI : 
            180.0; // Large uncertainty for very short baselines

        var relPosData = new RelativePositionUpdate
        {
            ITow = iTOW,
            RelPosN = relPosNMeters,
            RelPosE = relPosEMeters,
            RelPosD = relPosD_meters,
            RelPosLength = relPosLength,
            RelPosHeading = relPosHeading,
            AccN = accNMeters,
            AccE = accEMeters,
            AccD = accDMeters,
            AccLength = accLength,
            AccHeading = accHeading,
            RelPosValid = relPosValid,
            RelPosNormalized = relPosNormalized,
            CarrSoln = carrSoln,
            IsMoving = isMoving,
            RefPosMiss = refPosMiss,
            RefObsMiss = refObsMiss,
            RelPosHeadingValid = relPosHeadingValid
        };

        try
        {
            // Throttle dashboard updates
            var throttleInterval = TimeSpan.FromMilliseconds(1000.0 / SystemConfiguration.GnssDataRateDashboard);
            if (DateTime.UtcNow - _lastSentTime < throttleInterval)
                return; // Skip this update

            _lastSentTime = DateTime.UtcNow;
            await hubContext.Clients.All.SendAsync("RelativePositionUpdate", relPosData, stoppingToken);
            
            logger.LogDebug("📍 Relative position: Distance to base = {Distance:F3}m, Heading = {Heading:F1}°, CarrSoln = {CarrSoln}, Valid = {Valid}",
                relPosLength, relPosHeading, carrSoln, relPosValid);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to send relative position data to frontend");
        }
    }
}