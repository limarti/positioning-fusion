using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class SurveyInStatusParser
{
    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        logger.LogDebug("ProcessNavSvinMessage: Received {DataLength} bytes", data.Length);

        if (data.Length < 40)
        {
            logger.LogWarning("NAV-SVIN message too short: {Length} bytes, minimum 40 required", data.Length);
            return;
        }

        try
        {
            // Parse NAV-SVIN message structure
            var version = data[0];
            var reserved1 = BitConverter.ToUInt32(data, 1);
            var iTow = BitConverter.ToUInt32(data, 4);
            var dur = BitConverter.ToUInt32(data, 8);           // Survey-in duration in seconds
            var meanX = BitConverter.ToInt32(data, 12);         // Mean X coordinate (cm)
            var meanY = BitConverter.ToInt32(data, 16);         // Mean Y coordinate (cm)  
            var meanZ = BitConverter.ToInt32(data, 20);         // Mean Z coordinate (cm)
            var meanXHP = data[24];                             // Mean X high precision part (0.1mm)
            var meanYHP = data[25];                             // Mean Y high precision part (0.1mm)
            var meanZHP = data[26];                             // Mean Z high precision part (0.1mm)
            var reserved2 = data[27];
            var meanAcc = BitConverter.ToUInt32(data, 28);      // Mean position accuracy (0.1mm)
            var obs = BitConverter.ToUInt32(data, 32);          // Number of observations
            var valid = data[36];                               // Valid flags
            var active = data[37];                              // Active flags

            // Check Survey-In status flags
            var surveyInActive = (active & 0x01) != 0;          // Survey-In is active
            var surveyInValid = (valid & 0x01) != 0;            // Survey-In position is valid

            // Convert accuracy from 0.1mm to mm
            var accuracyMm = meanAcc * 0.1;

            // Log Survey-In status
            //if (surveyInActive)
            //{
            //    logger.LogInformation("📍 Survey-In ACTIVE: Duration={Duration}s, Observations={Obs}, Accuracy={Accuracy:F1}mm", dur, obs, accuracyMm);
            //}
            //else if (surveyInValid)
            //{
            //    logger.LogInformation("✅ Survey-In COMPLETED: Final accuracy={Accuracy:F1}mm, Total observations={Obs}", accuracyMm, obs);
            //}
            //else
            //{
            //    logger.LogInformation("❌ Survey-In INACTIVE: Duration={Duration}s, Observations={Obs}", dur, obs);
            //}

            // Send Survey-In status to frontend
            await hubContext.Clients.All.SendAsync("SurveyInStatus", new
            {
                Active = surveyInActive,
                Valid = surveyInValid,
                Duration = dur,
                Observations = obs,
                AccuracyMm = accuracyMm,
                Position = new
                {
                    X = meanX / 100.0 + meanXHP * 0.1e-3,  // Convert to meters
                    Y = meanY / 100.0 + meanYHP * 0.1e-3,  // Convert to meters
                    Z = meanZ / 100.0 + meanZHP * 0.1e-3   // Convert to meters
                },
                Timestamp = DateTime.UtcNow
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing NAV-SVIN message");
        }
    }
}