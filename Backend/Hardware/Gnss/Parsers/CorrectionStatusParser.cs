using Backend.Hubs;
using Backend.Configuration;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class CorrectionStatusParser
{
    private static DateTime _lastSentTime = DateTime.MinValue;

    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        logger.LogDebug("ProcessRxmCorMessage: Received {DataLength} bytes", data.Length);

        // Notify NavigationSatelliteParser that we received RXM-COR
        // This prevents it from sending synthetic SBAS corrections when we have authoritative data
        NavigationSatelliteParser.NotifyRxmCorReceived();

        if (data.Length < 16)
        {
            logger.LogWarning("RXM-COR message too short: {Length} bytes, minimum 16 required", data.Length);
            return;
        }

        try
        {
            // UBX-RXM-COR message structure
            var version = data[0];                                      // Message version (1)
            var corrFlags = BitConverter.ToUInt16(data, 2);            // Correction flags
            var msgType = BitConverter.ToUInt16(data, 4);              // Message type
            var msgSubType = BitConverter.ToUInt16(data, 6);           // Message sub-type
            var numMsgs = BitConverter.ToUInt16(data, 8);              // Number of correction messages
            var corrAge = BitConverter.ToUInt32(data, 10);             // Age of corrections (ms)
            var reserved1 = BitConverter.ToUInt16(data, 14);           // Reserved

            // Extract correction flags
            var correctionValid = (corrFlags & 0x01) != 0;            // Corrections are valid
            var correctionStale = (corrFlags & 0x02) != 0;            // Corrections are stale
            var sbas = (corrFlags & 0x10) != 0;                       // SBAS corrections
            var rtcm = (corrFlags & 0x20) != 0;                       // RTCM corrections
            var spartn = (corrFlags & 0x40) != 0;                     // SPARTN corrections
            var reserved = (corrFlags & 0x80) != 0;                   // Reserved flag

            // Determine primary correction source
            var correctionSource = "None";
            if (spartn) correctionSource = "SPARTN";
            else if (rtcm) correctionSource = "RTCM";
            else if (sbas) correctionSource = "SBAS";

            // Calculate correction status
            var correctionStatus = "Unknown";
            if (!correctionValid)
                correctionStatus = "Invalid";
            else if (correctionStale)
                correctionStatus = "Stale";
            else if (correctionValid)
                correctionStatus = "Valid";

            logger.LogDebug("🔧 RXM-COR: Version={Version}, Source={Source}, Status={Status}, Age={Age}ms, NumMsgs={NumMsgs}",
                version, correctionSource, correctionStatus, corrAge, numMsgs);

            // AUTHORITATIVE CORRECTION STATUS UPDATE
            // This is the official correction status from the receiver via RXM-COR messages.
            // RXM-COR is sent when the receiver actively processes correction streams:
            // - RTCM corrections from external base stations
            // - SPARTN corrections from commercial services
            // - SBAS corrections (though SBAS is sometimes not reported here)
            //
            // CONFLICT WARNING: NavigationSatelliteParser may also send synthetic SBAS correction
            // status updates based on satellite data. This RXM-COR data is authoritative when present,
            // but may race with the synthetic updates. Whichever arrives last wins.
            var correctionData = new CorrectionStatusUpdate
            {
                Version = version,
                CorrectionFlags = corrFlags,
                MessageType = msgType,
                MessageSubType = msgSubType,
                NumMessages = numMsgs,
                CorrectionAge = corrAge,
                CorrectionValid = correctionValid,
                CorrectionStale = correctionStale,
                SbasCorrections = sbas,
                RtcmCorrections = rtcm,
                SpartnCorrections = spartn,
                CorrectionSource = correctionSource,
                CorrectionStatus = correctionStatus,
                Timestamp = DateTime.UtcNow
            };

            // Throttle dashboard updates
            var throttleInterval = TimeSpan.FromMilliseconds(1000.0 / SystemConfiguration.GnssDataRateDashboard);
            if (DateTime.UtcNow - _lastSentTime < throttleInterval)
                return; // Skip this update

            _lastSentTime = DateTime.UtcNow;
            await hubContext.Clients.All.SendAsync("CorrectionStatusUpdate", correctionData, stoppingToken);

            logger.LogDebug("📡 Sent RXM-COR update to frontend: Source={Source}, Status={Status}",
                correctionSource, correctionStatus);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing RXM-COR message");
        }
    }
}

public class CorrectionStatusUpdate
{
    public byte Version { get; set; }
    public ushort CorrectionFlags { get; set; }
    public ushort MessageType { get; set; }
    public ushort MessageSubType { get; set; }
    public ushort NumMessages { get; set; }
    public uint CorrectionAge { get; set; }
    public bool CorrectionValid { get; set; }
    public bool CorrectionStale { get; set; }
    public bool SbasCorrections { get; set; }
    public bool RtcmCorrections { get; set; }
    public bool SpartnCorrections { get; set; }
    public string CorrectionSource { get; set; } = string.Empty;
    public string CorrectionStatus { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}