using Backend.Hardware.Gnss.Models;
using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss;

/// <summary>
/// Aggregates correction status from multiple sources (RXM-COR, NAV-SAT, NAV-PVT)
/// and produces a single authoritative correction status update.
///
/// Priority order (by correction type):
/// - SPARTN (PPP-RTK/SSR): Use RXM-COR.age
/// - RTCM (DGNSS/RTK/OSR): Use NAV-PVT.diffAge
/// - SBAS: Use NAV-SAT (NAV-SBAS.ionoAge not implemented)
/// - None: No corrections detected
/// </summary>
public static class CorrectionStatusAggregator
{
    private static readonly object _lock = new object();
    private static DateTime _lastSentTime = DateTime.MinValue;
    private static CorrectionStatusUpdate? _lastSentStatus = null;

    // Time thresholds for considering sources "stale"
    private const double RxmCorStaleSeconds = 5.0;
    private const double NavSatStaleSeconds = 5.0;
    private const double NavPvtStaleSeconds = 2.0;

    // Minimum time between updates (throttle)
    private const double MinUpdateIntervalSeconds = 1.0;

    /// <summary>
    /// Called by parsers after they update GnssDataStore.
    /// Checks all available sources and sends update if state changed.
    /// </summary>
    public static async Task ProcessUpdate(IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        try
        {
            lock (_lock)
            {
                // Throttle updates
                var timeSinceLastSend = (DateTime.UtcNow - _lastSentTime).TotalSeconds;
                if (timeSinceLastSend < MinUpdateIntervalSeconds)
                {
                    return;
                }
            }

            var (rxmCor, navSat, navPvt) = GnssDataStore.GetCorrectionSources();
            var now = DateTime.UtcNow;

            CorrectionStatusUpdate? update = null;

            // Priority 1: SPARTN (PPP-RTK/SSR) - Use RXM-COR.age
            if (rxmCor != null &&
                rxmCor.SpartnCorrections &&
                (now - rxmCor.ReceivedAt).TotalSeconds <= RxmCorStaleSeconds)
            {
                logger.LogDebug("📊 Aggregator: Using RXM-COR SPARTN (age: {Age:F1}s)", (now - rxmCor.ReceivedAt).TotalSeconds);

                update = new CorrectionStatusUpdate
                {
                    Version = rxmCor.Version,
                    CorrectionFlags = rxmCor.CorrectionFlags,
                    MessageType = rxmCor.MessageType,
                    MessageSubType = rxmCor.MessageSubType,
                    NumMessages = rxmCor.NumMessages,
                    CorrectionAge = rxmCor.CorrectionAge,
                    CorrectionValid = rxmCor.CorrectionValid,
                    CorrectionStale = rxmCor.CorrectionStale,
                    SbasCorrections = rxmCor.SbasCorrections,
                    RtcmCorrections = rxmCor.RtcmCorrections,
                    SpartnCorrections = rxmCor.SpartnCorrections,
                    CorrectionSource = rxmCor.CorrectionSource,
                    CorrectionStatus = rxmCor.CorrectionStatus,
                    Timestamp = DateTime.UtcNow
                };
            }
            // Priority 2: RTCM (DGNSS/RTK/OSR) - Use NAV-PVT.diffAge
            else if (navPvt != null &&
                     navPvt.DiffSoln &&
                     (now - navPvt.ReceivedAt).TotalSeconds <= NavPvtStaleSeconds)
            {
                logger.LogDebug("📊 Aggregator: Using NAV-PVT RTCM (age: {Age:F1}s, carrSoln={CarrSoln}, diffAge={DiffAge}ms)",
                    (now - navPvt.ReceivedAt).TotalSeconds, navPvt.CarrierSolution, navPvt.DiffAge);

                // Determine source from carrier solution
                string source;
                if (navPvt.CarrierSolution == UbxConstants.CARRIER_SOLUTION_FIXED ||
                    navPvt.CarrierSolution == UbxConstants.CARRIER_SOLUTION_FLOAT)
                {
                    source = "RTCM"; // RTK implies RTCM
                }
                else
                {
                    source = "DGPS"; // Could be RTCM or SBAS
                }

                update = new CorrectionStatusUpdate
                {
                    Version = 1,
                    CorrectionFlags = 0x21, // Valid + RTCM (assume RTCM)
                    MessageType = 0,
                    MessageSubType = 0,
                    NumMessages = 0,
                    CorrectionAge = navPvt.DiffAge, // Use diffAge from NAV-PVT (per UBX rule)
                    CorrectionValid = true,
                    CorrectionStale = false,
                    SbasCorrections = false,
                    RtcmCorrections = true,
                    SpartnCorrections = false,
                    CorrectionSource = source,
                    CorrectionStatus = "Valid (PVT)",
                    Timestamp = DateTime.UtcNow
                };
            }
            // Priority 3: SBAS - Use NAV-SAT (NAV-SBAS.ionoAge not implemented)
            else if (navSat != null &&
                     navSat.SbasInUse &&
                     navSat.DiffCorrInUse &&
                     (now - navSat.ReceivedAt).TotalSeconds <= NavSatStaleSeconds)
            {
                logger.LogDebug("📊 Aggregator: Using NAV-SAT SBAS (age: {Age:F1}s, diffCorr={Count})",
                    (now - navSat.ReceivedAt).TotalSeconds, navSat.DiffCorrSatellites);

                update = new CorrectionStatusUpdate
                {
                    Version = 1,
                    CorrectionFlags = 0x11, // Valid + SBAS
                    MessageType = 0,
                    MessageSubType = 0,
                    NumMessages = 0,
                    CorrectionAge = null, // NAV-SAT doesn't provide age (would need NAV-SBAS.ionoAge)
                    CorrectionValid = true,
                    CorrectionStale = false,
                    SbasCorrections = true,
                    RtcmCorrections = false,
                    SpartnCorrections = false,
                    CorrectionSource = "SBAS",
                    CorrectionStatus = "Valid (NAV-SAT)",
                    Timestamp = DateTime.UtcNow
                };
            }
            // Priority 4: Fallback to RXM-COR for RTCM/SBAS if NAV-PVT/NAV-SAT unavailable
            else if (rxmCor != null &&
                     (rxmCor.RtcmCorrections || rxmCor.SbasCorrections) &&
                     (now - rxmCor.ReceivedAt).TotalSeconds <= RxmCorStaleSeconds)
            {
                logger.LogDebug("📊 Aggregator: Using RXM-COR fallback (age: {Age:F1}s, source={Source})",
                    (now - rxmCor.ReceivedAt).TotalSeconds, rxmCor.CorrectionSource);

                update = new CorrectionStatusUpdate
                {
                    Version = rxmCor.Version,
                    CorrectionFlags = rxmCor.CorrectionFlags,
                    MessageType = rxmCor.MessageType,
                    MessageSubType = rxmCor.MessageSubType,
                    NumMessages = rxmCor.NumMessages,
                    CorrectionAge = rxmCor.CorrectionAge,
                    CorrectionValid = rxmCor.CorrectionValid,
                    CorrectionStale = rxmCor.CorrectionStale,
                    SbasCorrections = rxmCor.SbasCorrections,
                    RtcmCorrections = rxmCor.RtcmCorrections,
                    SpartnCorrections = rxmCor.SpartnCorrections,
                    CorrectionSource = rxmCor.CorrectionSource,
                    CorrectionStatus = rxmCor.CorrectionStatus,
                    Timestamp = DateTime.UtcNow
                };
            }
            // Priority 5: No corrections detected
            else
            {
                logger.LogDebug("📊 Aggregator: No corrections detected");

                update = new CorrectionStatusUpdate
                {
                    Version = 1,
                    CorrectionFlags = 0,
                    MessageType = 0,
                    MessageSubType = 0,
                    NumMessages = 0,
                    CorrectionAge = null,
                    CorrectionValid = false,
                    CorrectionStale = false,
                    SbasCorrections = false,
                    RtcmCorrections = false,
                    SpartnCorrections = false,
                    CorrectionSource = "None",
                    CorrectionStatus = "No corrections",
                    Timestamp = DateTime.UtcNow
                };
            }

            // Only send if status changed
            bool shouldSend = false;
            lock (_lock)
            {
                if (_lastSentStatus == null || HasStatusChanged(_lastSentStatus, update))
                {
                    _lastSentStatus = update;
                    _lastSentTime = DateTime.UtcNow;
                    shouldSend = true;
                }
            }

            if (shouldSend)
            {
                logger.LogInformation("📡 Aggregator: Sending update - Source={Source}, Status={Status}, Age={Age}",
                    update.CorrectionSource, update.CorrectionStatus,
                    update.CorrectionAge?.ToString() ?? "—");

                await hubContext.Clients.All.SendAsync("CorrectionStatusUpdate", update, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in CorrectionStatusAggregator.ProcessUpdate");
        }
    }

    private static bool HasStatusChanged(CorrectionStatusUpdate old, CorrectionStatusUpdate newStatus)
    {
        return old.CorrectionSource != newStatus.CorrectionSource ||
               old.CorrectionStatus != newStatus.CorrectionStatus ||
               old.CorrectionValid != newStatus.CorrectionValid ||
               old.CorrectionAge != newStatus.CorrectionAge;
    }
}
