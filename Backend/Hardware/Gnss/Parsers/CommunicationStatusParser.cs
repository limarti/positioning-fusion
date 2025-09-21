using Backend.Hubs;
using Backend.Hardware.Gnss;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hardware.Gnss.Parsers;

public static class CommunicationStatusParser
{
    public static async Task ProcessAsync(byte[] data, IHubContext<DataHub> hubContext, ILogger logger, CancellationToken stoppingToken)
    {
        try
        {
            if (data.Length < 8)
            {
                logger.LogWarning("MON-COMMS payload too short: {Length} bytes (minimum 8)", data.Length);
                return;
            }

            // UBX-MON-COMMS structure (variable length based on number of ports)
            // Header (8 bytes) + variable number of port blocks (40 bytes each)
            var version = data[0];                              // Message version (0x00 for this version)
            var nPorts = data[1];                              // Number of ports
            var txErrors = BitConverter.ToUInt16(data, 2);     // TX error bitmask
            var reserved1 = BitConverter.ToUInt32(data, 4);    // Reserved

            // Calculate expected length: 8 bytes header + (nPorts * 40 bytes per port)
            var expectedLength = 8 + (nPorts * 40);
            if (data.Length < expectedLength)
            {
                logger.LogWarning("MON-COMMS payload too short for {NPorts} ports: {Length} bytes (expected {Expected})", 
                    nPorts, data.Length, expectedLength);
                return;
            }

            logger.LogDebug("📊 MON-COMMS: version={Version}, nPorts={NPorts}, txErrors=0x{TxErrors:X4}", 
                version, nPorts, txErrors);

            var ports = new List<object>();

            // Parse port information blocks
            for (int i = 0; i < nPorts; i++)
            {
                int offset = 8 + (i * 40);
                
                var portId = BitConverter.ToUInt16(data, offset);        // Port identifier
                var txPending = BitConverter.ToUInt16(data, offset + 2); // Bytes pending in TX buffer
                var txBytes = BitConverter.ToUInt32(data, offset + 4);   // Number of bytes ever sent
                var txUsage = data[offset + 8];                          // TX buffer usage peak %
                var txPeakUsage = data[offset + 9];                      // TX buffer peak usage %
                var rxPending = BitConverter.ToUInt16(data, offset + 10); // Bytes pending in RX buffer
                var rxBytes = BitConverter.ToUInt32(data, offset + 12);   // Number of bytes ever received
                var rxUsage = data[offset + 16];                         // RX buffer usage %
                var rxPeakUsage = data[offset + 17];                     // RX buffer peak usage %
                var overrunErrs = BitConverter.ToUInt16(data, offset + 18); // Number of RX buffer overrun errors
                
                // Protocol-specific message counts (8 protocols * 2 bytes each = 16 bytes)
                var msgs = new List<ushort>();
                for (int p = 0; p < 8; p++)
                {
                    var msgCount = BitConverter.ToUInt16(data, offset + 20 + (p * 2));
                    msgs.Add(msgCount);
                }
                
                var reserved2 = BitConverter.ToUInt32(data, offset + 36); // Reserved

                // Protocol names for display
                var protocolNames = new[] { "UBX", "NMEA", "RTCM2", "Reserved", "Reserved", "RTCM3", "Reserved", "Reserved" };

                var protocolCounts = new Dictionary<string, ushort>();
                for (int p = 0; p < Math.Min(msgs.Count, protocolNames.Length); p++)
                {
                    if (msgs[p] > 0) // Only include protocols with messages
                    {
                        protocolCounts[protocolNames[p]] = msgs[p];
                    }
                }

                var portInfo = new
                {
                    PortId = portId,
                    TxPending = txPending,
                    TxBytes = txBytes,
                    TxUsage = txUsage,
                    TxPeakUsage = txPeakUsage,
                    RxPending = rxPending,
                    RxBytes = rxBytes,
                    RxUsage = rxUsage,
                    RxPeakUsage = rxPeakUsage,
                    OverrunErrors = overrunErrs,
                    ProtocolCounts = protocolCounts
                };

                ports.Add(portInfo);

                // Get port name for logging
                var portName = portId switch
                {
                    0 => "I2C",
                    1 => "UART1",
                    2 => "UART2", 
                    3 => "USB",
                    4 => "SPI",
                    _ => $"Port{portId}"
                };

                logger.LogDebug("📊 Port {PortName}: TX={TxBytes} bytes ({TxPending} pending), RX={RxBytes} bytes ({RxPending} pending), Protocols: {Protocols}", 
                    portName, txBytes, txPending, rxBytes, rxPending, 
                    string.Join(", ", protocolCounts.Select(kv => $"{kv.Key}:{kv.Value}")));
            }

            // Send communication status to frontend via SignalR
            await hubContext.Clients.All.SendAsync("CommunicationStatusUpdate", new CommunicationStatusUpdate
            {
                Version = version,
                NPorts = nPorts,
                TxErrors = txErrors,
                Ports = ports,
                Timestamp = DateTime.UtcNow
            }, stoppingToken);

            logger.LogDebug("📡 Sent MON-COMMS update with {NPorts} ports to frontend", nPorts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing MON-COMMS message");
        }
    }
}

public class CommunicationStatusUpdate
{
    public byte Version { get; set; }
    public byte NPorts { get; set; }
    public ushort TxErrors { get; set; }
    public List<object> Ports { get; set; } = new();
    public DateTime Timestamp { get; set; }
}