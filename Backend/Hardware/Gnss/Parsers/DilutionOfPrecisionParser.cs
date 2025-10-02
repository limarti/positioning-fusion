using Backend.Hubs;
using Backend.Hardware.Gnss.Models;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class DilutionOfPrecisionParser
{
    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        logger.LogDebug("ProcessNavDopMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 18)
        {
            logger.LogWarning("NAV-DOP message too short: {Length} bytes, minimum 18 required", data.Length);
            return;
        }

        var iTow = BitConverter.ToUInt32(data, 0);
        var gDop = BitConverter.ToUInt16(data, 4) * 0.01;
        var pDop = BitConverter.ToUInt16(data, 6) * 0.01;
        var tDop = BitConverter.ToUInt16(data, 8) * 0.01;
        var vDop = BitConverter.ToUInt16(data, 10) * 0.01;
        var hDop = BitConverter.ToUInt16(data, 12) * 0.01;
        var nDop = BitConverter.ToUInt16(data, 14) * 0.01;
        var eDop = BitConverter.ToUInt16(data, 16) * 0.01;

        //logger.LogInformation("DOP: iTow={ITow}, GDOP={GDOP:F2}, PDOP={PDOP:F2}, HDOP={HDOP:F2}, VDOP={VDOP:F2}", iTow, gDop, pDop, hDop, vDop);

        var dopData = new DopUpdate
        {
            ITow = iTow,
            GeometricDop = gDop,
            PositionDop = pDop,
            TimeDop = tDop,
            VerticalDop = vDop,
            HorizontalDop = hDop,
            NorthingDop = nDop,
            EastingDop = eDop
        };

        try
        {
            await hubContext.Clients.All.SendAsync("DopUpdate", dopData, stoppingToken);
            logger.LogDebug("✅ DOP data sent to frontend");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to send DOP data to frontend");
        }
    }
}