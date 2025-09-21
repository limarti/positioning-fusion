using Backend.Hubs;
using Backend.Hardware.Gnss;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class NavigationSignalParser
{
    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        try
        {
            if (data.Length < 8)
            {
                logger.LogWarning("NAV-SIG payload too short: {Length} bytes (minimum 8)", data.Length);
                return;
            }

            // UBX-NAV-SIG structure (variable length based on number of signals)
            // Header (8 bytes) + variable number of signal blocks (16 bytes each)
            var iTOW = BitConverter.ToUInt32(data, 0);           // GPS time of week (ms)
            var version = data[4];                               // Message version (0 for this version)
            var numSigs = data[5];                              // Number of signals
            var reserved1 = BitConverter.ToUInt16(data, 6);     // Reserved

            if (data.Length < 8 + (numSigs * 16))
            {
                logger.LogWarning("NAV-SIG payload too short for {NumSigs} signals: {Length} bytes (expected {Expected})", 
                    numSigs, data.Length, 8 + (numSigs * 16));
                return;
            }

            logger.LogDebug("🛰️ NAV-SIG: iTOW={ITOW}, version={Version}, numSigs={NumSigs}", 
                iTOW, version, numSigs);

            var signals = new List<object>();

            // Parse signal information blocks
            for (int i = 0; i < numSigs; i++)
            {
                int offset = 8 + (i * 16);
                
                var gnssId = data[offset];                      // GNSS identifier
                var svId = data[offset + 1];                   // Satellite identifier
                var sigId = data[offset + 2];                  // Signal identifier
                var freqId = data[offset + 3];                 // Frequency identifier
                var prRes = BitConverter.ToInt16(data, offset + 4);     // Pseudorange residual (0.1 m)
                var cno = data[offset + 6];                    // Carrier-to-noise ratio (dB-Hz)
                var qualityInd = data[offset + 7];             // Signal quality indicator
                var corrSource = data[offset + 8];             // Correction source
                var ionoModel = data[offset + 9];              // Ionospheric model
                var sigFlags = BitConverter.ToUInt16(data, offset + 10);   // Signal flags
                var reserved2 = BitConverter.ToUInt32(data, offset + 12);  // Reserved

                var gnssName = gnssId switch
                {
                    UbxConstants.GNSS_ID_GPS => "GPS",
                    UbxConstants.GNSS_ID_SBAS => "SBAS",
                    UbxConstants.GNSS_ID_GALILEO => "Galileo",
                    UbxConstants.GNSS_ID_BEIDOU => "BeiDou",
                    UbxConstants.GNSS_ID_IMES => "IMES",
                    UbxConstants.GNSS_ID_QZSS => "QZSS",
                    UbxConstants.GNSS_ID_GLONASS => "GLONASS",
                    _ => $"Unknown({gnssId})"
                };

                // Extract signal flags
                var healthFlag = (sigFlags & 0x03);           // Signal health
                var prSmoothed = (sigFlags & 0x04) != 0;      // Pseudorange smoothed
                var prUsed = (sigFlags & 0x08) != 0;          // Pseudorange used
                var crUsed = (sigFlags & 0x10) != 0;          // Carrier range used
                var doUsed = (sigFlags & 0x20) != 0;          // Doppler used
                var prCorrUsed = (sigFlags & 0x40) != 0;      // Pseudorange corrections used
                var crCorrUsed = (sigFlags & 0x80) != 0;      // Carrier range corrections used
                var doCorrUsed = (sigFlags & 0x100) != 0;     // Doppler corrections used

                var signal = new
                {
                    GnssId = gnssId,
                    GnssName = gnssName,
                    SvId = svId,
                    SigId = sigId,
                    FreqId = freqId,
                    PrRes = prRes * 0.1, // Convert to meters
                    Cno = cno,
                    QualityInd = qualityInd,
                    CorrSource = corrSource,
                    IonoModel = ionoModel,
                    HealthFlag = healthFlag,
                    PrSmoothed = prSmoothed,
                    PrUsed = prUsed,
                    CrUsed = crUsed,
                    DoUsed = doUsed,
                    PrCorrUsed = prCorrUsed,
                    CrCorrUsed = crCorrUsed,
                    DoCorrUsed = doCorrUsed
                };

                signals.Add(signal);

                logger.LogDebug("🛰️ Signal {Index}: {GnssName} SV{SvId} SigId={SigId} CNO={Cno} dB-Hz", 
                    i + 1, gnssName, svId, sigId, cno);
            }

            // Send signal information to frontend via SignalR
            await hubContext.Clients.All.SendAsync("NavigationSignalUpdate", new NavigationSignalUpdate
            {
                ITOW = iTOW,
                Version = version,
                NumSignals = numSigs,
                Signals = signals,
                Timestamp = DateTime.UtcNow
            }, stoppingToken);

            logger.LogDebug("📡 Sent NAV-SIG update with {NumSigs} signals to frontend", numSigs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing NAV-SIG message");
        }
    }
}

public class NavigationSignalUpdate
{
    public uint ITOW { get; set; }
    public byte Version { get; set; }
    public byte NumSignals { get; set; }
    public List<object> Signals { get; set; } = new();
    public DateTime Timestamp { get; set; }
}