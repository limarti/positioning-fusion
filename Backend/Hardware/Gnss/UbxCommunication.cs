using System.IO.Ports;

namespace Backend.Hardware.Gnss;

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
    /// <param name="serialPort">Serial port to communicate through</param>
    /// <param name="messageClass">UBX message class (e.g., 0x06 for CFG)</param>
    /// <param name="messageId">UBX message ID (e.g., 0x00 for CFG-PRT)</param>
    /// <param name="payload">Message payload bytes</param>
    /// <returns>True if ACK received, false otherwise</returns>
    public async Task<bool> SendUbxCommandAsync(SerialPort serialPort, byte messageClass, byte messageId, byte[] payload)
    {
        try
        {
            var ubxMessage = CreateUbxMessage(messageClass, messageId, payload);

            Console.WriteLine($"    → Sending UBX command: Class=0x{messageClass:X2}, ID=0x{messageId:X2}, Length={payload.Length}");
            Console.WriteLine($"    → UBX bytes: {BitConverter.ToString(ubxMessage)}");

            // Clear buffers
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            // Send UBX message
            serialPort.Write(ubxMessage, 0, ubxMessage.Length);

            // Wait for ACK/NAK response
            var response = await WaitForUbxResponseAsync(serialPort, messageClass, messageId);

            if (response.HasValue)
            {
                if (response.Value)
                {
                    Console.WriteLine($"    ✓ UBX command acknowledged (ACK)");
                    return true;
                }
                else
                {
                    Console.WriteLine($"    ✗ UBX command rejected (NAK)");
                    return false;
                }
            }
            else
            {
                Console.WriteLine($"    ✗ No UBX response received (timeout)");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ✗ Error sending UBX command: {ex.Message}");
            _logger.LogError(ex, "Failed to send UBX command Class=0x{MessageClass:X2}, ID=0x{MessageId:X2}", messageClass, messageId);
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
    /// <returns>True for ACK, False for NAK, null for timeout</returns>
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
                Console.WriteLine($"    ! Error reading UBX response: {ex.Message}");
                break;
            }

            await Task.Delay(50);
        }

        // Log any received data for debugging
        if (buffer.Count > 0)
        {
            Console.WriteLine($"    → Received {buffer.Count} bytes but no valid UBX ACK/NAK found");
            Console.WriteLine($"    → Data: {BitConverter.ToString(buffer.ToArray())}");
        }

        return null; // Timeout
    }

    /// <summary>
    /// Parses received bytes looking for UBX ACK/NAK messages
    /// </summary>
    private bool? ParseUbxMessages(List<byte> buffer, byte originalClass, byte originalId)
    {
        for (int i = 0; i < buffer.Count - 7; i++) // Minimum UBX message is 8 bytes
        {
            // Look for UBX sync pattern
            if (buffer[i] == UBX_SYNC_CHAR_1 && buffer[i + 1] == UBX_SYNC_CHAR_2)
            {
                if (i + 7 >= buffer.Count) break; // Not enough data yet

                var messageClass = buffer[i + 2];
                var messageId = buffer[i + 3];
                var length = (ushort)(buffer[i + 4] | (buffer[i + 5] << 8));

                // Check if we have complete message
                var totalLength = 8 + length; // Header (6) + payload + checksum (2)
                if (i + totalLength > buffer.Count) break; // Incomplete message

                // Check for ACK (Class=0x05, ID=0x01) or NAK (Class=0x05, ID=0x00)
                if (messageClass == 0x05 && (messageId == 0x01 || messageId == 0x00))
                {
                    if (length >= 2) // ACK/NAK payload should be at least 2 bytes
                    {
                        var ackClass = buffer[i + 6];
                        var ackId = buffer[i + 7];

                        var isAck = messageId == 0x01;
                        Console.WriteLine($"    ← UBX response: {(isAck ? "ACK" : "NAK")} for Class=0x{ackClass:X2}, ID=0x{ackId:X2}");

                        // Add detailed NAK analysis
                        if (!isAck)
                        {
                            AnalyzeNakResponse(originalClass, originalId, ackClass, ackId);

                            // For CFG-PRT polling, try different approach
                            if (ackClass == 0x06 && ackId == 0x00)
                            {
                                Console.WriteLine($"    → CFG-PRT poll NAKed - this might be normal for some u-blox generations");
                                Console.WriteLine($"    → Will try direct baud rate change instead of polling first");
                            }
                        }

                        // Check if this ACK/NAK is for our command
                        if (ackClass == originalClass && ackId == originalId)
                        {
                            // Remove processed bytes from buffer
                            buffer.RemoveRange(0, i + totalLength);
                            return isAck; // True for ACK, False for NAK
                        }
                    }
                }

                // Remove this processed message from buffer
                buffer.RemoveRange(0, i + totalLength);
                i = -1; // Reset search from beginning
            }
        }

        return null; // No matching ACK/NAK found yet
    }

    /// <summary>
    /// Creates UBX CFG-PRT command to configure UART port settings including baud rate
    /// </summary>
    public byte[] CreateCfgPrtCommand(byte portId, uint baudRate)
    {
        var payload = new byte[20];
        var index = 0;

        // Port ID (1 byte) - 1 = UART1, 0 = I2C, 3 = USB, 4 = SPI
        payload[index++] = portId;

        // Reserved (1 byte)
        payload[index++] = 0x00;

        // TX Ready (2 bytes) - 0x0000 = disabled
        payload[index++] = 0x00;
        payload[index++] = 0x00;

        // Mode (4 bytes) - UART mode settings
        // Correct u-blox format: bits 6-7=charLen, bits 9-11=parity, bits 12-13=nStopBits
        // charLen: 3=8bit, parity: 4=none, nStopBits: 0=1 stop bit
        var mode = (uint)(0x000008C0); // Standard 8N1 mode for u-blox
        payload[index++] = (byte)(mode & 0xFF);
        payload[index++] = (byte)((mode >> 8) & 0xFF);
        payload[index++] = (byte)((mode >> 16) & 0xFF);
        payload[index++] = (byte)((mode >> 24) & 0xFF);

        // Baud Rate (4 bytes, little-endian)
        payload[index++] = (byte)(baudRate & 0xFF);
        payload[index++] = (byte)((baudRate >> 8) & 0xFF);
        payload[index++] = (byte)((baudRate >> 16) & 0xFF);
        payload[index++] = (byte)((baudRate >> 24) & 0xFF);

        // Input Protocol Mask (2 bytes) - Enable UBX and NMEA
        payload[index++] = 0x01; // UBX only for cleaner communication
        payload[index++] = 0x00;

        // Output Protocol Mask (2 bytes) - Enable UBX and NMEA
        payload[index++] = 0x01; // UBX only initially, can add NMEA later
        payload[index++] = 0x00;

        // Flags (2 bytes) - No special flags
        payload[index++] = 0x00;
        payload[index++] = 0x00;

        // Reserved (2 bytes)
        payload[index++] = 0x00;
        payload[index++] = 0x00;

        return payload;
    }

    /// <summary>
    /// Creates UBX MON-VER command to request version information (useful for testing communication)
    /// </summary>
    public byte[] CreateMonVerCommand()
    {
        return new byte[0]; // MON-VER has no payload
    }

    /// <summary>
    /// Creates UBX CFG-PRT poll command to query current port configuration
    /// This is useful to see current settings before modifying them
    /// </summary>
    public byte[] CreateCfgPrtPollCommand(byte portId)
    {
        return new byte[] { portId }; // Poll specific port
    }

    /// <summary>
    /// Sends CFG-PRT poll command and parses the response to understand current port configuration
    /// </summary>
    public async Task<CfgPrtResponse?> PollCfgPrtAsync(SerialPort serialPort, byte portId)
    {
        try
        {
            Console.WriteLine($"    → Polling CFG-PRT for port {portId}...");

            var pollPayload = CreateCfgPrtPollCommand(portId);
            var ubxMessage = CreateUbxMessage(0x06, 0x00, pollPayload);

            // Clear buffers more aggressively
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            // Wait for buffer to clear
            await Task.Delay(200);

            // Send poll command
            Console.WriteLine($"    → Sending CFG-PRT poll: {BitConverter.ToString(ubxMessage)}");
            serialPort.Write(ubxMessage, 0, ubxMessage.Length);

            // Wait for CFG-PRT response (not ACK/NAK)
            return await WaitForCfgPrtResponseAsync(serialPort);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ✗ Error polling CFG-PRT: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Waits for and parses CFG-PRT response message
    /// </summary>
    private async Task<CfgPrtResponse?> WaitForCfgPrtResponseAsync(SerialPort serialPort)
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

                    // Try to parse CFG-PRT response
                    var result = ParseCfgPrtResponse(buffer);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ! Error reading CFG-PRT response: {ex.Message}");
                break;
            }

            await Task.Delay(50);
        }

        return null;
    }

    /// <summary>
    /// Parses CFG-PRT response from received bytes
    /// </summary>
    private CfgPrtResponse? ParseCfgPrtResponse(List<byte> buffer)
    {
        Console.WriteLine($"    → Parsing {buffer.Count} bytes for CFG-PRT response...");

        // Log first 100 bytes for debugging
        if (buffer.Count > 0)
        {
            var preview = buffer.Take(Math.Min(100, buffer.Count)).ToArray();
            Console.WriteLine($"    → Buffer preview: {BitConverter.ToString(preview)}");
        }

        for (int i = 0; i < buffer.Count - 7; i++)
        {
            // Look for UBX sync pattern
            if (buffer[i] == UBX_SYNC_CHAR_1 && buffer[i + 1] == UBX_SYNC_CHAR_2)
            {
                Console.WriteLine($"    → Found UBX sync at position {i}");

                if (i + 7 >= buffer.Count) break;

                var messageClass = buffer[i + 2];
                var messageId = buffer[i + 3];
                var length = (ushort)(buffer[i + 4] | (buffer[i + 5] << 8));

                Console.WriteLine($"    → UBX message: Class=0x{messageClass:X2}, ID=0x{messageId:X2}, Length={length}");

                // Check for CFG-PRT response (Class=0x06, ID=0x00)
                if (messageClass == 0x06 && messageId == 0x00)
                {
                    var totalLength = 8 + length;
                    if (i + totalLength > buffer.Count)
                    {
                        Console.WriteLine($"    → Incomplete CFG-PRT message, need {totalLength} bytes but have {buffer.Count - i}");
                        break;
                    }

                    if (length >= 20) // CFG-PRT response should be 20 bytes
                    {
                        var payload = new byte[length];
                        Array.Copy(buffer.ToArray(), i + 6, payload, 0, length);

                        // Verify checksum
                        var expectedChecksum = CalculateUbxChecksum(messageClass, messageId, payload);
                        var actualCkA = buffer[i + 6 + length];
                        var actualCkB = buffer[i + 6 + length + 1];

                        if (expectedChecksum.ck_a == actualCkA && expectedChecksum.ck_b == actualCkB)
                        {
                            Console.WriteLine($"    ✓ CFG-PRT checksum valid");
                            var response = ParseCfgPrtPayload(payload);
                            if (response != null)
                            {
                                // Remove processed bytes
                                buffer.RemoveRange(0, i + totalLength);
                                return response;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"    ✗ CFG-PRT checksum invalid: expected {expectedChecksum.ck_a:X2},{expectedChecksum.ck_b:X2}, got {actualCkA:X2},{actualCkB:X2}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"    ✗ CFG-PRT payload too short: {length} bytes");
                    }
                }

                // Remove processed message or continue searching
                var msgTotalLength = 8 + length;
                if (i + msgTotalLength <= buffer.Count)
                {
                    buffer.RemoveRange(0, i + msgTotalLength);
                    i = -1;
                }
                else
                {
                    // Can't remove incomplete message, continue searching
                    break;
                }
            }
            else if (buffer[i] == 0x24) // '$' character - NMEA data
            {
                // Skip NMEA sentences - look for end of line
                int nmeaEnd = i;
                while (nmeaEnd < buffer.Count && buffer[nmeaEnd] != 0x0A) // Find '\n'
                {
                    nmeaEnd++;
                }
                if (nmeaEnd < buffer.Count)
                {
                    // Remove entire NMEA sentence
                    var nmeaLength = nmeaEnd - i + 1;
                    Console.WriteLine($"    → Skipping NMEA sentence of {nmeaLength} bytes at position {i}");
                    buffer.RemoveRange(i, nmeaLength);
                    i = -1; // Restart search
                }
                else
                {
                    // Incomplete NMEA, keep searching
                    break;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parses CFG-PRT payload into structured response
    /// </summary>
    private CfgPrtResponse? ParseCfgPrtPayload(byte[] payload)
    {
        if (payload.Length < 20) return null;

        try
        {
            var response = new CfgPrtResponse
            {
                PortId = payload[0],
                Reserved1 = payload[1],
                TxReady = (ushort)(payload[2] | (payload[3] << 8)),
                Mode = (uint)(payload[4] | (payload[5] << 8) | (payload[6] << 16) | (payload[7] << 24)),
                BaudRate = (uint)(payload[8] | (payload[9] << 8) | (payload[10] << 16) | (payload[11] << 24)),
                InProtoMask = (ushort)(payload[12] | (payload[13] << 8)),
                OutProtoMask = (ushort)(payload[14] | (payload[15] << 8)),
                Flags = (ushort)(payload[16] | (payload[17] << 8)),
                Reserved2 = (ushort)(payload[18] | (payload[19] << 8))
            };

            Console.WriteLine($"    ✓ CFG-PRT Response for Port {response.PortId}:");
            Console.WriteLine($"      - Baud Rate: {response.BaudRate}");
            Console.WriteLine($"      - Mode: 0x{response.Mode:X8}");
            Console.WriteLine($"      - Input Protocols: 0x{response.InProtoMask:X4} (NMEA={((response.InProtoMask & 0x01) != 0 ? "✓" : "✗")}, UBX={((response.InProtoMask & 0x02) != 0 ? "✓" : "✗")})");
            Console.WriteLine($"      - Output Protocols: 0x{response.OutProtoMask:X4} (NMEA={((response.OutProtoMask & 0x01) != 0 ? "✓" : "✗")}, UBX={((response.OutProtoMask & 0x02) != 0 ? "✓" : "✗")})");
            Console.WriteLine($"      - Flags: 0x{response.Flags:X4}");

            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ✗ Error parsing CFG-PRT payload: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Alternative CFG-PRT command that preserves current protocol settings and only changes baud rate
    /// This is safer as it doesn't change protocol masks that might be configured differently
    /// </summary>
    public byte[] CreateCfgPrtBaudOnlyCommand(byte portId, uint baudRate)
    {
        var payload = new byte[20];
        var index = 0;

        // Port ID (1 byte)
        payload[index++] = portId;

        // Reserved (1 byte)
        payload[index++] = 0x00;

        // TX Ready (2 bytes) - disabled
        payload[index++] = 0x00;
        payload[index++] = 0x00;

        // Mode (4 bytes) - Standard 8N1
        payload[index++] = 0xC0;
        payload[index++] = 0x08;
        payload[index++] = 0x00;
        payload[index++] = 0x00;

        // Baud Rate (4 bytes, little-endian)
        payload[index++] = (byte)(baudRate & 0xFF);
        payload[index++] = (byte)((baudRate >> 8) & 0xFF);
        payload[index++] = (byte)((baudRate >> 16) & 0xFF);
        payload[index++] = (byte)((baudRate >> 24) & 0xFF);

        // Input Protocol Mask (2 bytes) - Keep both NMEA and UBX
        payload[index++] = 0x03; // NMEA (0x01) + UBX (0x02)
        payload[index++] = 0x00;

        // Output Protocol Mask (2 bytes) - Keep both NMEA and UBX
        payload[index++] = 0x03; // NMEA (0x01) + UBX (0x02)
        payload[index++] = 0x00;

        // Flags (2 bytes)
        payload[index++] = 0x00;
        payload[index++] = 0x00;

        // Reserved (2 bytes)
        payload[index++] = 0x00;
        payload[index++] = 0x00;

        return payload;
    }

    /// <summary>
    /// Creates UBX CFG-PRT command to enable UBX protocol on current port while keeping NMEA
    /// This is useful when UBX is disabled and we need to enable it first
    /// </summary>
    public byte[] CreateCfgPrtEnableUbxCommand(byte portId, uint currentBaudRate)
    {
        var payload = new byte[20];
        var index = 0;

        // Port ID (1 byte) - 1 = UART1
        payload[index++] = portId;

        // Reserved (1 byte)
        payload[index++] = 0x00;

        // TX Ready (2 bytes) - 0x0000 = disabled
        payload[index++] = 0x00;
        payload[index++] = 0x00;

        // Mode (4 bytes) - UART mode settings (8N1)
        var mode = (uint)((3 << 6) | (4 << 9) | (0 << 12)); // 8N1
        payload[index++] = (byte)(mode & 0xFF);
        payload[index++] = (byte)((mode >> 8) & 0xFF);
        payload[index++] = (byte)((mode >> 16) & 0xFF);
        payload[index++] = (byte)((mode >> 24) & 0xFF);

        // Keep current Baud Rate (4 bytes, little-endian)
        payload[index++] = (byte)(currentBaudRate & 0xFF);
        payload[index++] = (byte)((currentBaudRate >> 8) & 0xFF);
        payload[index++] = (byte)((currentBaudRate >> 16) & 0xFF);
        payload[index++] = (byte)((currentBaudRate >> 24) & 0xFF);

        // Input Protocol Mask (2 bytes) - Enable NMEA and UBX
        payload[index++] = 0x03; // NMEA (0x01) + UBX (0x02)
        payload[index++] = 0x00;

        // Output Protocol Mask (2 bytes) - Enable NMEA and UBX
        payload[index++] = 0x03; // NMEA (0x01) + UBX (0x02)
        payload[index++] = 0x00;

        // Flags (2 bytes) - Extended timeout disabled
        payload[index++] = 0x00;
        payload[index++] = 0x00;

        // Reserved (2 bytes)
        payload[index++] = 0x00;
        payload[index++] = 0x00;

        return payload;
    }

    /// <summary>
    /// Provides detailed analysis of NAK responses to help diagnose configuration issues
    /// </summary>
    private void AnalyzeNakResponse(byte originalClass, byte originalId, byte nakClass, byte nakId)
    {
        Console.WriteLine($"    ⚠ NAK Analysis:");
        Console.WriteLine($"      - Command that was rejected: Class=0x{nakClass:X2}, ID=0x{nakId:X2}");

        // Analyze specific command types
        if (nakClass == 0x06 && nakId == 0x00) // CFG-PRT
        {
            Console.WriteLine($"      - CFG-PRT command rejected. Possible reasons:");
            Console.WriteLine($"        • Invalid port ID (common values: 0=I2C, 1=UART1, 3=USB, 4=SPI)");
            Console.WriteLine($"        • Invalid baud rate (module may not support the requested rate)");
            Console.WriteLine($"        • Invalid mode field (check parity, data bits, stop bits)");
            Console.WriteLine($"        • Protocol mask conflict (NMEA/UBX combination not supported)");
            Console.WriteLine($"        • Reserved fields not set to expected values");
            Console.WriteLine($"        • Trying to configure non-existent port (e.g., no USB on this model)");
            Console.WriteLine($"      - Recommendation: Poll CFG-PRT first to see current configuration");
        }
        else if (nakClass == 0x0A && nakId == 0x04) // MON-VER
        {
            Console.WriteLine($"      - MON-VER command rejected. Possible reasons:");
            Console.WriteLine($"        • UBX protocol not properly enabled");
            Console.WriteLine($"        • Firmware doesn't support this command");
            Console.WriteLine($"        • Module is not a u-blox device");
        }
        else if (nakClass == 0x06) // CFG class
        {
            Console.WriteLine($"      - Configuration command rejected (CFG class)");
            Console.WriteLine($"        • Configuration may be locked or protected");
            Console.WriteLine($"        • Invalid parameter values");
            Console.WriteLine($"        • Command not supported by this firmware version");
        }
        else
        {
            Console.WriteLine($"      - Unknown command class rejected");
            Console.WriteLine($"        • Command may not be supported by this module");
            Console.WriteLine($"        • Check u-blox documentation for your specific module");
        }

        // General suggestions
        Console.WriteLine($"      - General troubleshooting:");
        Console.WriteLine($"        • Verify module model and firmware version with MON-VER");
        Console.WriteLine($"        • Check if configuration is persistent (may need CFG-CFG save)");
        Console.WriteLine($"        • Some modules require specific sequence of commands");
        Console.WriteLine($"        • Factory reset might be needed if configuration is corrupted");
    }

    /// <summary>
    /// Attempts to enable UBX protocol using NMEA command as fallback
    /// Some u-blox modules need this before they respond to UBX binary commands
    /// </summary>
    public async Task<bool> EnableUbxProtocolAsync(SerialPort serialPort, byte portId, uint baudRate)
    {
        try
        {
            Console.WriteLine($"    → Attempting to enable UBX protocol using NMEA command...");

            // Send NMEA command to enable UBX output
            // $PUBX,41,portID,inProto,outProto,baudrate,autobauding*checksum
            var enableUbxCommand = $"$PUBX,41,{portId},0003,0003,{baudRate},0";
            var checksum = CalculateNmeaChecksum(enableUbxCommand.Substring(1));
            enableUbxCommand += $"*{checksum:X2}\r\n";

            Console.WriteLine($"    → Sending NMEA UBX enable command: {enableUbxCommand.Trim()}");

            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();
            serialPort.Write(enableUbxCommand);

            // Wait for response
            await Task.Delay(1000);

            // Now try UBX CFG-PRT to enable UBX protocol
            Console.WriteLine($"    → Following up with UBX CFG-PRT command...");
            var cfgPrtPayload = CreateCfgPrtEnableUbxCommand(portId, baudRate);
            return await SendUbxCommandAsync(serialPort, 0x06, 0x00, cfgPrtPayload);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ✗ Error enabling UBX protocol: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Calculate NMEA checksum (XOR of all characters between $ and *)
    /// </summary>
    private static byte CalculateNmeaChecksum(string sentence)
    {
        byte checksum = 0;
        foreach (char c in sentence)
        {
            checksum ^= (byte)c;
        }
        return checksum;
    }

    /// <summary>
    /// Creates a smart CFG-PRT command based on current configuration
    /// This preserves existing settings and only modifies the baud rate
    /// </summary>
    public byte[] CreateSmartCfgPrtCommand(CfgPrtResponse currentConfig, uint newBaudRate)
    {
        var payload = new byte[20];
        var index = 0;

        // Port ID - keep same
        payload[index++] = currentConfig.PortId;

        // Reserved - keep same
        payload[index++] = currentConfig.Reserved1;

        // TX Ready - keep same
        payload[index++] = (byte)(currentConfig.TxReady & 0xFF);
        payload[index++] = (byte)((currentConfig.TxReady >> 8) & 0xFF);

        // Mode - keep same
        payload[index++] = (byte)(currentConfig.Mode & 0xFF);
        payload[index++] = (byte)((currentConfig.Mode >> 8) & 0xFF);
        payload[index++] = (byte)((currentConfig.Mode >> 16) & 0xFF);
        payload[index++] = (byte)((currentConfig.Mode >> 24) & 0xFF);

        // Baud Rate - NEW VALUE
        payload[index++] = (byte)(newBaudRate & 0xFF);
        payload[index++] = (byte)((newBaudRate >> 8) & 0xFF);
        payload[index++] = (byte)((newBaudRate >> 16) & 0xFF);
        payload[index++] = (byte)((newBaudRate >> 24) & 0xFF);

        // Input Protocol Mask - keep same
        payload[index++] = (byte)(currentConfig.InProtoMask & 0xFF);
        payload[index++] = (byte)((currentConfig.InProtoMask >> 8) & 0xFF);

        // Output Protocol Mask - keep same
        payload[index++] = (byte)(currentConfig.OutProtoMask & 0xFF);
        payload[index++] = (byte)((currentConfig.OutProtoMask >> 8) & 0xFF);

        // Flags - keep same
        payload[index++] = (byte)(currentConfig.Flags & 0xFF);
        payload[index++] = (byte)((currentConfig.Flags >> 8) & 0xFF);

        // Reserved - keep same
        payload[index++] = (byte)(currentConfig.Reserved2 & 0xFF);
        payload[index++] = (byte)((currentConfig.Reserved2 >> 8) & 0xFF);

        Console.WriteLine($"    → Smart CFG-PRT command preserves all settings except baud rate:");
        Console.WriteLine($"      - Port: {currentConfig.PortId} (unchanged)");
        Console.WriteLine($"      - Mode: 0x{currentConfig.Mode:X8} (unchanged)");
        Console.WriteLine($"      - Protocols: In=0x{currentConfig.InProtoMask:X4}, Out=0x{currentConfig.OutProtoMask:X4} (unchanged)");
        Console.WriteLine($"      - Baud: {currentConfig.BaudRate} → {newBaudRate}");

        return payload;
    }
}

/// <summary>
/// Represents a parsed CFG-PRT response from the GNSS module
/// </summary>
public class CfgPrtResponse
{
    public byte PortId { get; set; }
    public byte Reserved1 { get; set; }
    public ushort TxReady { get; set; }
    public uint Mode { get; set; }
    public uint BaudRate { get; set; }
    public ushort InProtoMask { get; set; }
    public ushort OutProtoMask { get; set; }
    public ushort Flags { get; set; }
    public ushort Reserved2 { get; set; }
}