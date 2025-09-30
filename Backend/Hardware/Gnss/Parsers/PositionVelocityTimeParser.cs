using Backend.Hubs;
using Backend.Configuration;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class PositionVelocityTimeParser
{
    private static DateTime _lastSentTime = DateTime.MinValue;
    private static byte _lastSentSecond = 255; // Track last sent GNSS second

    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        logger.LogDebug("ProcessNavPvtMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 84)
        {
            logger.LogWarning("NAV-PVT message too short: {Length} bytes, minimum 84 required", data.Length);
            return;
        }

        var iTow = BitConverter.ToUInt32(data, 0);
        var year = BitConverter.ToUInt16(data, 4);
        var month = data[6];
        var day = data[7];
        var hour = data[8];
        var min = data[9];
        var sec = data[10];
        var valid = data[11];
        var nano = BitConverter.ToInt32(data, 16);
        var fixType = data[20];
        var flags = data[21];
        var flags2 = data[22];
        var numSV = data[23];

        // Parse coordinates from NAV-PVT (7 decimal precision, ~11mm)
        var lon = BitConverter.ToInt32(data, 24) * 1e-7;
        var lat = BitConverter.ToInt32(data, 28) * 1e-7;
        var height = BitConverter.ToInt32(data, 32); // Height above ellipsoid (mm)
        var hMSL = BitConverter.ToInt32(data, 36); // Height above MSL (mm)

        var hAcc = BitConverter.ToUInt32(data, 40);
        var vAcc = BitConverter.ToUInt32(data, 44);

        var gnssFixOk = (flags & 0x01) != 0;
        var diffSoln = (flags & 0x02) != 0;
        var psmState = (flags >> 2) & 0x07;
        var headVehValid = (flags & 0x20) != 0;
        var carrSoln = (flags >> 6) & 0x03;

        // Get enhanced fix type using fixType, diffSoln, and carrSoln
        var fixTypeString = GetEnhancedFixTypeLabel(fixType, diffSoln, carrSoln);

        var carrierString = carrSoln switch
        {
            UbxConstants.CARRIER_SOLUTION_NONE => "None",
            UbxConstants.CARRIER_SOLUTION_FLOAT => "Float",
            UbxConstants.CARRIER_SOLUTION_FIXED => "Fixed",
            _ => $"Unknown({carrSoln})"
        };

        // Convert GNSS time to Unix epoch timestamp (milliseconds)
        long gnssTimestamp = 0;
        // Check if we have basic date and time validity (bits 0x01 and 0x02)
        bool dateValid = (valid & 0x01) != 0;
        bool timeValid = (valid & 0x02) != 0;

        if (dateValid && timeValid)
        {
            try
            {
                // Validate ranges before creating DateTime
                if (year >= 1970 && year <= 3000 &&
                    month >= 1 && month <= 12 &&
                    day >= 1 && day <= 31 &&
                    hour <= 23 && min <= 59 && sec <= 59)
                {
                    var gnssDateTime = new DateTime(year, month, day, hour, min, sec, DateTimeKind.Utc);
                    gnssTimestamp = ((DateTimeOffset)gnssDateTime).ToUnixTimeMilliseconds();

                    // Update static GNSS time for session folder renaming
                    GnssService.UpdateGnssTime(gnssDateTime);
                }
            }
            catch
            {
                gnssTimestamp = 0; // Invalid date/time construction
            }
        }

        var pvtData = new PvtUpdate
        {
            ITow = iTow,
            Year = year,
            Month = month,
            Day = day,
            Hour = hour,
            Minute = min,
            Second = sec,
            TimeValid = valid,
            GnssTimestamp = gnssTimestamp,
            FixType = fixType,
            FixTypeString = fixTypeString,
            GnssFixOk = gnssFixOk,
            DifferentialSolution = diffSoln,
            NumSatellites = numSV,
            Longitude = lon,
            Latitude = lat,
            HeightEllipsoid = height,
            HeightMSL = hMSL,
            HorizontalAccuracy = hAcc / 1000.0, // Convert mm to meters
            VerticalAccuracy = vAcc / 1000.0, // Convert mm to meters
            CarrierSolution = carrSoln
        };

        try
        {
            // Always send if the GNSS second has changed (for clock updates)
            bool shouldSendForClock = sec != _lastSentSecond && gnssTimestamp > 0;

            // Throttle dashboard updates for position data
            var throttleInterval = TimeSpan.FromMilliseconds(1000.0 / SystemConfiguration.GnssDataRateDashboard);
            bool shouldSendForPosition = DateTime.UtcNow - _lastSentTime >= throttleInterval;

            if (shouldSendForClock || shouldSendForPosition)
            {
                _lastSentTime = DateTime.UtcNow;
                _lastSentSecond = sec;
                logger.LogDebug("📤 Sending PvtUpdate to frontend: Fix = {Fix}, Sats = {Sats}, CarrierSoln = {CarrSoln}",
                    pvtData.FixTypeString, pvtData.NumSatellites, carrierString);
                await hubContext.Clients.All.SendAsync("PvtUpdate", pvtData, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to send PVT data to frontend");
        }
    }

    private static string GetEnhancedFixTypeLabel(byte fixType, bool diffSoln, int carrSoln)
    {
        // Handle No Fix case first
        if (fixType == UbxConstants.FIX_TYPE_NO_FIX)
            return "No Fix";

        // RTK solutions take priority (carrSoln > 0)
        if (carrSoln == UbxConstants.CARRIER_SOLUTION_FIXED)
        {
            return fixType == UbxConstants.FIX_TYPE_2D ? "RTK Fix 2D" : "RTK Fix";
        }
        
        if (carrSoln == UbxConstants.CARRIER_SOLUTION_FLOAT)
        {
            return fixType == UbxConstants.FIX_TYPE_2D ? "RTK Float 2D" : "RTK Float";
        }

        // Non-RTK solutions (carrSoln == 0)
        if (diffSoln)
        {
            return fixType == UbxConstants.FIX_TYPE_2D ? "DGPS 2D" : "DGPS";
        }

        // Single point solutions
        if (fixType == UbxConstants.FIX_TYPE_2D)
            return "Single 2D";
        
        if (fixType == UbxConstants.FIX_TYPE_3D)
            return "Single 3D";

        // Fallback for other fix types
        return fixType switch
        {
            UbxConstants.FIX_TYPE_DEAD_RECKONING => "Dead Reckoning",
            UbxConstants.FIX_TYPE_GNSS_DR => "GNSS+DR",
            UbxConstants.FIX_TYPE_TIME_ONLY => "Time Only",
            _ => $"Unknown({fixType})"
        };
    }
}