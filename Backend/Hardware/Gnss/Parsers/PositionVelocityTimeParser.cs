using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class PositionVelocityTimeParser
{
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
        var tAcc = BitConverter.ToUInt32(data, 12);
        var nano = BitConverter.ToInt32(data, 16);
        var fixType = data[20];
        var flags = data[21];
        var flags2 = data[22];
        var numSV = data[23];
        var lon = BitConverter.ToInt32(data, 24) * 1e-7;
        var lat = BitConverter.ToInt32(data, 28) * 1e-7;
        var height = BitConverter.ToInt32(data, 32);
        var hMSL = BitConverter.ToInt32(data, 36);
        var hAcc = BitConverter.ToUInt32(data, 40);
        var vAcc = BitConverter.ToUInt32(data, 44);

        var gnssFixOk = (flags & 0x01) != 0;
        var diffSoln = (flags & 0x02) != 0;
        var psmState = (flags >> 2) & 0x07;
        var headVehValid = (flags & 0x20) != 0;
        var carrSoln = (flags >> 6) & 0x03;

        // Get human-readable fix type
        var fixTypeString = fixType switch
        {
            UbxConstants.FIX_TYPE_NO_FIX => "No Fix",
            UbxConstants.FIX_TYPE_DEAD_RECKONING => "Dead Reckoning",
            UbxConstants.FIX_TYPE_2D => "2D Fix",
            UbxConstants.FIX_TYPE_3D => "3D Fix",
            UbxConstants.FIX_TYPE_GNSS_DR => "GNSS+DR",
            UbxConstants.FIX_TYPE_TIME_ONLY => "Time Only",
            _ => $"Unknown({fixType})"
        };

        var carrierString = carrSoln switch
        {
            UbxConstants.CARRIER_SOLUTION_NONE => "None",
            UbxConstants.CARRIER_SOLUTION_FLOAT => "Float",
            UbxConstants.CARRIER_SOLUTION_FIXED => "Fixed",
            _ => $"Unknown({carrSoln})"
        };

        //_logger.LogInformation("NAV-PVT: iTow={iTow}, {DateTime}, Fix={FixType}({FixCode}), Carrier={Carrier}, Sats={NumSV}", iTow, $"{year:D4}-{month:D2}-{day:D2} {hour:D2}:{min:D2}:{sec:D2}", fixTypeString, fixType, carrierString, numSV);
        //_logger.LogInformation("Position: Lat={Lat:F7}°, Lon={Lon:F7}°, Alt={Alt:F1}m, hAcc={HAcc}mm, vAcc={VAcc}mm", lat, lon, hMSL / 1000.0, hAcc, vAcc);
        //_logger.LogDebug("Flags: gnssFixOk={GnssFixOk}, diffSoln={DiffSoln}, timeValid=0x{TimeValid:X2}", gnssFixOk, diffSoln, valid);

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
            TimeAccuracy = tAcc,
            FixType = fixType,
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
            await hubContext.Clients.All.SendAsync("PvtUpdate", pvtData, stoppingToken);
            //logger.LogInformation("✅ PVT data sent to frontend: {FixType}, {NumSV} sats, Lat={Lat:F7}, Lon={Lon:F7}", fixTypeString, numSV, lat, lon);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to send PVT data to frontend");
        }
    }
}