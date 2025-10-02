using Backend.Hubs;
using Backend.Configuration;
using Backend.Hardware.Gnss.Models;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class ReceiverVersionParser
{
    private static DateTime _lastSentTime = DateTime.MinValue;

    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation("📋 Processing MON-VER message: {Length} bytes", data.Length);

            if (data.Length < 40)
            {
                logger.LogWarning("MON-VER message too short: {Length} bytes", data.Length);
                return;
            }

            // Parse software version (first 30 bytes, null-terminated string)
            var swVersionBytes = data.Take(30).TakeWhile(b => b != 0).ToArray();
            var swVersion = global::System.Text.Encoding.ASCII.GetString(swVersionBytes);

            // Parse hardware version (next 10 bytes, null-terminated string)
            var hwVersionBytes = data.Skip(30).Take(10).TakeWhile(b => b != 0).ToArray();
            var hwVersion = global::System.Text.Encoding.ASCII.GetString(hwVersionBytes);

            logger.LogInformation("🔍 ZED-X20P Software Version: {SwVersion}", swVersion);
            logger.LogInformation("🔍 ZED-X20P Hardware Version: {HwVersion}", hwVersion);

            // Parse extensions (if any)
            if (data.Length > 40)
            {
                var remainingBytes = data.Skip(40).ToArray();
                var extensions = global::System.Text.Encoding.ASCII.GetString(remainingBytes.TakeWhile(b => b != 0).ToArray());
                if (!string.IsNullOrEmpty(extensions))
                {
                    logger.LogInformation("🔍 ZED-X20P Extensions: {Extensions}", extensions);
                }
            }

            // Send version info to frontend
            var versionData = new VersionUpdate
            {
                SoftwareVersion = swVersion,
                HardwareVersion = hwVersion,
                ReceiverType = "ZED-X20P"
            };

            // Throttle dashboard updates
            var throttleInterval = TimeSpan.FromMilliseconds(1000.0 / SystemConfiguration.GnssDataRateDashboard);
            if (DateTime.UtcNow - _lastSentTime < throttleInterval)
                return; // Skip this update

            _lastSentTime = DateTime.UtcNow;
            await hubContext.Clients.All.SendAsync("VersionUpdate", versionData, stoppingToken);
            logger.LogInformation("✅ Version data sent to frontend");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing MON-VER message");
        }
    }
}