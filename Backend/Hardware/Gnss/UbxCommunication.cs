using System.IO.Ports;

namespace Backend.Hardware.Gnss;

/// <summary>
/// UBX binary protocol communication for u-blox GNSS modules
/// Currently unused but kept for potential future functionality
/// </summary>
public class UbxCommunication
{
    private readonly ILogger<UbxCommunication> _logger;
    private const byte UBX_SYNC_CHAR_1 = 0xB5;
    private const byte UBX_SYNC_CHAR_2 = 0x62;
    private const int UBX_RESPONSE_TIMEOUT_MS = 2000;

    public UbxCommunication(ILogger<UbxCommunication> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sends a UBX command and waits for acknowledgment
    /// </summary>
    public async Task<bool> SendUbxCommandAsync(SerialPort serialPort, byte messageClass, byte messageId, byte[] payload)
    {
        try
        {
            var ubxMessage = CreateUbxMessage(messageClass, messageId, payload);

            _logger.LogDebug("Sending UBX command: Class=0x{MessageClass:X2}, ID=0x{MessageId:X2}, Length={Length}",
                messageClass, messageId, payload.Length);

            // Clear buffers
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            // Send UBX message
            serialPort.Write(ubxMessage, 0, ubxMessage.Length);

            // Wait for ACK/NAK response
            var response = await WaitForUbxResponseAsync(serialPort, messageClass, messageId);

            if (response.HasValue && response.Value)
            {
                _logger.LogDebug("UBX command acknowledged");
                return true;
            }
            else
            {
                _logger.LogDebug("UBX command rejected or timeout");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send UBX command Class=0x{MessageClass:X2}, ID=0x{MessageId:X2}",
                messageClass, messageId);
            return false;
        }
    }

    /// <summary>
    /// Creates a complete UBX message with sync chars, header, payload, and checksum
    /// </summary>
    private byte[] CreateUbxMessage(byte messageClass, byte messageId, byte[] payload)
    {
        var message = new List<byte>();

        // Sync chars
        message.Add(UBX_SYNC_CHAR_1);
        message.Add(UBX_SYNC_CHAR_2);

        // Message class and ID
        message.Add(messageClass);
        message.Add(messageId);

        // Payload length (little-endian)
        var length = (ushort)payload.Length;
        message.Add((byte)(length & 0xFF));
        message.Add((byte)(length >> 8));

        // Payload
        message.AddRange(payload);

        // Calculate checksum (Fletcher-8 algorithm)
        var checksum = CalculateUbxChecksum(messageClass, messageId, payload);
        message.Add(checksum.ck_a);
        message.Add(checksum.ck_b);

        return message.ToArray();
    }

    /// <summary>
    /// Calculates UBX checksum using Fletcher-8 algorithm
    /// </summary>
    private (byte ck_a, byte ck_b) CalculateUbxChecksum(byte messageClass, byte messageId, byte[] payload)
    {
        byte ck_a = 0;
        byte ck_b = 0;

        // Include class and ID in checksum
        ck_a += messageClass;
        ck_b += ck_a;
        ck_a += messageId;
        ck_b += ck_a;

        // Include length in checksum
        var length = (ushort)payload.Length;
        ck_a += (byte)(length & 0xFF);
        ck_b += ck_a;
        ck_a += (byte)(length >> 8);
        ck_b += ck_a;

        // Include payload in checksum
        foreach (var b in payload)
        {
            ck_a += b;
            ck_b += ck_a;
        }

        return (ck_a, ck_b);
    }

    /// <summary>
    /// Waits for UBX ACK/NAK response
    /// </summary>
    private async Task<bool?> WaitForUbxResponseAsync(SerialPort serialPort, byte originalClass, byte originalId)
    {
        var buffer = new List<byte>();
        var endTime = DateTime.UtcNow.AddMilliseconds(UBX_RESPONSE_TIMEOUT_MS);

        while (DateTime.UtcNow < endTime)
        {
            try
            {
                if (serialPort.BytesToRead > 0)
                {
                    var bytesToRead = Math.Min(serialPort.BytesToRead, 256);
                    var tempBuffer = new byte[bytesToRead];
                    var bytesRead = serialPort.Read(tempBuffer, 0, bytesToRead);

                    for (int i = 0; i < bytesRead; i++)
                    {
                        buffer.Add(tempBuffer[i]);
                    }

                    // Try to parse UBX messages from buffer
                    var result = ParseUbxMessages(buffer, originalClass, originalId);
                    if (result.HasValue)
                    {
                        return result.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error reading UBX response");
                break;
            }

            await Task.Delay(50);
        }

        return null; // Timeout
    }

    /// <summary>
    /// Parses received bytes looking for UBX ACK/NAK messages
    /// </summary>
    private bool? ParseUbxMessages(List<byte> buffer, byte originalClass, byte originalId)
    {
        for (int i = 0; i < buffer.Count - 7; i++)
        {
            // Look for UBX sync pattern
            if (buffer[i] == UBX_SYNC_CHAR_1 && buffer[i + 1] == UBX_SYNC_CHAR_2)
            {
                if (i + 7 >= buffer.Count) break;

                var messageClass = buffer[i + 2];
                var messageId = buffer[i + 3];
                var length = (ushort)(buffer[i + 4] | (buffer[i + 5] << 8));

                // Check if we have complete message
                var totalLength = 8 + length;
                if (i + totalLength > buffer.Count) break;

                // Check for ACK (Class=0x05, ID=0x01) or NAK (Class=0x05, ID=0x00)
                if (messageClass == 0x05 && (messageId == 0x01 || messageId == 0x00))
                {
                    if (length >= 2)
                    {
                        var ackClass = buffer[i + 6];
                        var ackId = buffer[i + 7];

                        // Check if this ACK/NAK is for our command
                        if (ackClass == originalClass && ackId == originalId)
                        {
                            buffer.RemoveRange(0, i + totalLength);
                            return messageId == 0x01; // True for ACK, False for NAK
                        }
                    }
                }

                // Remove processed message from buffer
                buffer.RemoveRange(0, i + totalLength);
                i = -1; // Reset search from beginning
            }
        }

        return null; // No matching ACK/NAK found yet
    }

    /// <summary>
    /// Creates UBX MON-VER command to request version information
    /// </summary>
    public byte[] CreateMonVerCommand()
    {
        return new byte[0]; // MON-VER has no payload
    }

    /// <summary>
    /// Calculate NMEA checksum (XOR of all characters between $ and *)
    /// </summary>
    public static byte CalculateNmeaChecksum(string sentence)
    {
        byte checksum = 0;
        foreach (char c in sentence)
        {
            checksum ^= (byte)c;
        }
        return checksum;
    }
}