using Backend.Hubs;
using Backend.Configuration;
using Backend.Hardware.Gnss.Models;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class CorrectionStatusParser
{
    private static int _totalRxmCorReceived = 0;

    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        _totalRxmCorReceived++;

        logger.LogInformation("🔧 RXM-COR received! Total count: {Count}, Data length: {DataLength} bytes",
            _totalRxmCorReceived, data.Length);

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

            // Store data in centralized store
            var rxmCorData = new RxmCorData
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
                CorrectionStatus = correctionStatus
            };

            GnssDataStore.UpdateRxmCor(rxmCorData);

            // Let aggregator decide if update should be sent
            await CorrectionStatusAggregator.ProcessUpdate(hubContext, logger, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing RXM-COR message");
        }
    }
}