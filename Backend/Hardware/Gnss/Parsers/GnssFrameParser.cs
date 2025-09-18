using Microsoft.Extensions.Logging;

namespace Backend.Hardware.Gnss.Parsers;

public enum FrameKind { Ubx, Rtcm3, Nmea }

public class GnssFrameParser
{
    private readonly ILogger<GnssFrameParser> _logger;

    public GnssFrameParser(ILogger<GnssFrameParser> logger)
    {
        _logger = logger;
    }

    public bool TryFindNextFrame(List<byte> dataBuffer, out FrameKind kind, out int start, out int totalLen, out int partialNeeded)
    {
        kind = default;
        start = -1;
        totalLen = -1;
        partialNeeded = 0;

        var ubx = FindUbxCandidate(dataBuffer);
        var rtcm = FindRtcm3Candidate(dataBuffer);
        var nmea = FindNmeaCandidate(dataBuffer);

        (FrameKind k, int s, int t, int partial)? winner = null;

        void Consider((FrameKind k, int s, int t, int partial)? c)
        {
            if (c is { } x && x.s >= 0)
                winner = winner is null || x.s < winner.Value.s ? x : winner;
        }

        Consider(ubx);
        Consider(rtcm);
        Consider(nmea);

        if (winner is null)
            return false;

        (kind, start, totalLen, partialNeeded) = winner.Value;
        return true;
    }

    private (FrameKind k, int s, int t, int partial)? FindUbxCandidate(List<byte> dataBuffer)
    {
        // UBX: 0xB5 0x62 [cls][id][lenL][lenH] payload ... [CK_A][CK_B]
        for (int i = 0; i + 5 < dataBuffer.Count; i++)
        {
            if (dataBuffer[i] != 0xB5 || dataBuffer[i + 1] != 0x62) continue;

            // need at least header to read length
            if (i + 6 > dataBuffer.Count) 
            {
                _logger.LogDebug("⏳ UBX partial header at {Pos}: need {Need} more bytes", i, 6 - (i + 6 - dataBuffer.Count));
                return (FrameKind.Ubx, i, 6, 6 - dataBuffer.Count + i);
            }

            int len = dataBuffer[i + 4] | (dataBuffer[i + 5] << 8);
            if (len < 0 || len > 1024) 
            {
                _logger.LogDebug("🚫 UBX candidate at {Pos}: invalid length {Len}", i, len);
                continue;
            }

            int total = 6 + len + 2;
            if (i + total > dataBuffer.Count)
            {
                _logger.LogDebug("⏳ UBX partial frame at {Pos}: need {Need} more bytes (payload={PayloadLen}, total={Total})", 
                    i, (i + total) - dataBuffer.Count, len, total);
                return (FrameKind.Ubx, i, total, (i + total) - dataBuffer.Count);
            }

            if (ValidateUbxChecksum(dataBuffer, i, total))
            {
                _logger.LogDebug("✅ Valid UBX frame found at {Pos}: payload={PayloadLen}, total={Total}", i, len, total);
                return (FrameKind.Ubx, i, total, 0);
            }
            else
            {
                _logger.LogDebug("❌ UBX candidate at {Pos}: checksum validation failed (payload={PayloadLen})", i, len);
            }

            // bad checksum — skip this sync and keep scanning
        }
        return null;
    }

    private (FrameKind k, int s, int t, int partial)? FindRtcm3Candidate(List<byte> dataBuffer)
    {
        // RTCM3: 0xD3 [Rsv(6b)|Len(10b)] payload CRC24Q (3 bytes)
        for (int i = 0; i + 2 < dataBuffer.Count; i++)
        {
            if (dataBuffer[i] != 0xD3) continue;

            byte b1 = dataBuffer[i + 1];
            byte b2 = dataBuffer[i + 2];

            // Upper 6 bits of b1 must be 0
            if ((b1 & 0xFC) != 0)
            {
                _logger.LogDebug("🚫 RTCM3 candidate at {Pos}: invalid reserved bits in b1=0x{B1:X2}", i, b1);
                continue;
            }

            int payloadLen = ((b1 & 0x03) << 8) | b2;
            if (payloadLen <= 0 || payloadLen > 1024)
            {
                _logger.LogDebug("🚫 RTCM3 candidate at {Pos}: invalid payload length {Len}", i, payloadLen);
                continue;
            }

            int total = 3 + payloadLen + 3;
            if (i + total > dataBuffer.Count)
            {
                _logger.LogDebug("⏳ RTCM3 partial frame at {Pos}: need {Need} more bytes (payload={PayloadLen}, total={Total})", 
                    i, (i + total) - dataBuffer.Count, payloadLen, total);
                return (FrameKind.Rtcm3, i, total, (i + total) - dataBuffer.Count);
            }

            if (ValidateRtcmCrc24Q(dataBuffer, i, total))
            {
                //_logger.LogInformation("✅ Valid RTCM3 frame found at {Pos}: payload={PayloadLen}, total={Total}", i, payloadLen, total);
                return (FrameKind.Rtcm3, i, total, 0);
            }
            else
            {
                _logger.LogDebug("❌ RTCM3 candidate at {Pos}: CRC validation failed (payload={PayloadLen})", i, payloadLen);
            }
            // bad CRC — keep scanning
        }
        return null;
    }

    private (FrameKind k, int s, int t, int partial)? FindNmeaCandidate(List<byte> dataBuffer)
    {
        // NMEA: '$' ... *hh \r\n  — ASCII only, checksum validated.
        // We only try this if there is clearly a '$' in view.
        int dollar = dataBuffer.IndexOf((byte)'$');
        if (dollar < 0) return null;

        // Must end with CRLF to be complete
        int cr = dataBuffer.IndexOf((byte)'\r', dollar + 1);
        if (cr < 0 || cr + 1 >= dataBuffer.Count) return (FrameKind.Nmea, dollar, 0, 1); // partial; need at least CRLF

        if (dataBuffer[cr + 1] != (byte)'\n') return null;

        int end = cr + 2;
        int len = end - dollar;
        if (len < 9) return null; // too short to be valid

        // Validate ASCII and checksum
        var span = dataBuffer.GetRange(dollar, len).ToArray();
        if (!IsAscii(span)) return null;
        if (!ValidateNmeaChecksum(span)) return null;

        return (FrameKind.Nmea, dollar, len, 0);
    }

    private bool ValidateUbxChecksum(List<byte> dataBuffer, int start, int total)
    {
        byte ckA = 0, ckB = 0;
        int payloadLen = dataBuffer[start + 4] | (dataBuffer[start + 5] << 8);
        int end = start + 6 + payloadLen;
        for (int j = start + 2; j < end; j++)
        {
            ckA = (byte)(ckA + dataBuffer[j]);
            ckB = (byte)(ckB + ckA);
        }
        return ckA == dataBuffer[end] && ckB == dataBuffer[end + 1];
    }

    private bool ValidateRtcmCrc24Q(List<byte> dataBuffer, int start, int total)
    {
        // CRC over header(3) + payload, excluding the 3 CRC bytes at the end
        int crcLen = total - 3;
        uint calc = Crc24Q(dataBuffer, start, crcLen);
        uint got = (uint)(dataBuffer[start + total - 3] << 16 | dataBuffer[start + total - 2] << 8 | dataBuffer[start + total - 1]);
        return calc == got;
    }

    private static bool IsAscii(byte[] bytes)
    {
        foreach (var b in bytes)
        {
            if (b is < 0x09 or > 0x7E && b != 0x0D && b != 0x0A) return false;
        }
        return true;
    }

    private static bool ValidateNmeaChecksum(byte[] asciiWithCrlf)
    {
        // Expect: $XXXX*HH\r\n
        // Find '*'
        int star = Array.LastIndexOf(asciiWithCrlf, (byte)'*');
        if (star <= 0) return false;
        if (star + 2 >= asciiWithCrlf.Length) return false; // need two hex chars + CRLF

        byte cs = 0;
        for (int i = 1; i < star; i++)
            cs ^= asciiWithCrlf[i];

        bool hexOk = TryHexByte(asciiWithCrlf[star + 1], asciiWithCrlf[star + 2], out byte got);
        if (!hexOk) return false;

        return cs == got;
    }

    private static bool TryHexByte(byte hi, byte lo, out byte val)
    {
        val = 0;
        int h = FromHex(hi);
        int l = FromHex(lo);
        if (h < 0 || l < 0) return false;
        val = (byte)((h << 4) | l);
        return true;

        static int FromHex(byte c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }
    }

    private static uint Crc24Q(List<byte> buf, int start, int length)
    {
        // Polynomial 0x1864CFB, init 0x000000 (per RTCM standard)
        const uint poly = 0x1864CFB;
        uint crc = 0;
        for (int i = 0; i < length; i++)
        {
            crc ^= (uint)buf[start + i] << 16;
            for (int b = 0; b < 8; b++)
            {
                crc <<= 1;
                if ((crc & 0x1000000) != 0)
                    crc ^= poly;
            }
            crc &= 0xFFFFFF;
        }
        return crc & 0xFFFFFF;
    }
}