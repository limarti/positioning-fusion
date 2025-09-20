using Backend.Hubs;
using Backend.Configuration;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class NavigationSatelliteParser
{
    private static DateTime _lastSentTime = DateTime.MinValue;

    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        logger.LogDebug("ProcessNavSatMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 8)
        {
            logger.LogWarning("NAV-SAT message too short: {Length} bytes, minimum 8 required", data.Length);
            return;
        }

        var iTow = BitConverter.ToUInt32(data, 0);
        var version = data[4];
        var numSvs = data[5];

        //logger.LogInformation("NAV-SAT: iTow={iTow}, version={Version}, numSvs={NumSvs}, dataLength={DataLength}", iTow, version, numSvs, data.Length);

        // Check if we have enough data for all satellites
        var expectedLength = 8 + (numSvs * 12);
        if (data.Length < expectedLength)
        {
            logger.LogWarning("NAV-SAT message incomplete: expected {Expected} bytes, got {Actual} bytes for {NumSvs} satellites",
                expectedLength, data.Length, numSvs);
            return;
        }

        var satellites = new List<SatelliteInfo>();
        var constellationCounts = new Dictionary<string, int>();

        for (int i = 0; i < numSvs && (8 + i * 12) + 11 < data.Length; i++)
        {
            var offset = 8 + i * 12;

            var gnssId = data[offset];
            var svId = data[offset + 1];
            var cno = data[offset + 2];
            var elev = (sbyte)data[offset + 3];
            var azim = BitConverter.ToInt16(data, offset + 4);
            var prRes = BitConverter.ToInt16(data, offset + 6);
            var flags = BitConverter.ToUInt32(data, offset + 8);

            var qualityInd = (flags >> 0) & 0x7;
            var svUsed = (flags & 0x8) != 0;
            var health = (flags >> 4) & 0x3;
            var diffCorr = (flags & 0x40) != 0;
            var smoothed = (flags & 0x80) != 0;

            var gnssName = GnssParserUtils.GetGnssName(gnssId);

            // Count satellites by constellation
            constellationCounts[gnssName] = constellationCounts.GetValueOrDefault(gnssName, 0) + 1;

            logger.LogDebug("Satellite {Index}: {Constellation} {SvId}, C/N0={Cno}, Elev={Elev}°, Az={Az}°, Used={Used}",
                i, gnssName, svId, cno, elev, azim, svUsed);

            satellites.Add(new SatelliteInfo
            {
                GnssId = gnssId,
                GnssName = gnssName,
                SvId = svId,
                Cno = cno,
                Elevation = elev,
                Azimuth = azim,
                PseudorangeResidual = prRes * 0.1, // Convert to meters
                QualityIndicator = qualityInd,
                SvUsed = svUsed,
                Health = health,
                DifferentialCorrection = diffCorr,
                Smoothed = smoothed
            });
        }

        // Log constellation summary
        //var constellationSummary = string.Join(", ", constellationCounts.Select(kv => $"{kv.Key}:{kv.Value}"));
        //logger.LogInformation("Parsed {TotalSats} satellites: {ConstellationSummary}", satellites.Count, constellationSummary);

        var satelliteData = new SatelliteUpdate
        {
            ITow = iTow,
            NumSatellites = numSvs,
            Satellites = satellites
        };

        try
        {
            // Throttle dashboard updates
            var throttleInterval = TimeSpan.FromMilliseconds(1000.0 / SystemConfiguration.GnssDataRateDashboard);
            if (DateTime.UtcNow - _lastSentTime < throttleInterval)
                return; // Skip this update

            _lastSentTime = DateTime.UtcNow;
            await hubContext.Clients.All.SendAsync("SatelliteUpdate", satelliteData, stoppingToken);
            //logger.LogInformation("✅ Satellite data sent to frontend: {NumSats} satellites", numSvs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to send satellite data to frontend");
        }
    }
}