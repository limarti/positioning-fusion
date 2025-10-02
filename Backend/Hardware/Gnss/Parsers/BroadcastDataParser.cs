using Backend.Hubs;
using Backend.Configuration;
using Backend.Hardware.Gnss.Models;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class BroadcastDataParser
{
    private static DateTime _lastSentTime = DateTime.MinValue;

    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        logger.LogDebug("ProcessRxmSfrbxMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 8)
        {
            logger.LogWarning("RXM-SFRBX message too short: {Length} bytes, minimum 8 required", data.Length);
            return;
        }

        var gnssId = data[0];
        var svId = data[1];
        var freqId = data[2];
        var numWords = data[3];
        var chn = data[4];
        var version = data[5];

        var gnssName = GnssParserUtils.GetGnssName(gnssId);

        //_logger.LogInformation("RXM-SFRBX: {Constellation} SV{SvId}, FreqId={FreqId}, Words={NumWords}, Channel={Channel}, Version={Version}", gnssName, svId, freqId, numWords, chn, version);

        // For broadcast navigation data, we'll extract satellite information
        // Note: This is simplified - full SFRBX parsing would decode the actual navigation message
        var satelliteInfo = new BroadcastDataUpdate
        {
            GnssId = gnssId,
            GnssName = gnssName,
            SvId = svId,
            FrequencyId = freqId,
            Channel = chn,
            MessageLength = data.Length,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            // Throttle dashboard updates
            var throttleInterval = TimeSpan.FromMilliseconds(1000.0 / SystemConfiguration.GnssDataRateDashboard);
            if (DateTime.UtcNow - _lastSentTime < throttleInterval)
                return; // Skip this update

            _lastSentTime = DateTime.UtcNow;
            await hubContext.Clients.All.SendAsync("BroadcastDataUpdate", satelliteInfo, stoppingToken);
            //_logger.LogInformation("✅ Broadcast navigation data sent to frontend: {Constellation} SV{SvId}", gnssName, svId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to send broadcast data to frontend");
        }
    }
}