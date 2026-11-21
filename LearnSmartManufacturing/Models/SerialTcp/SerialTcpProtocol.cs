using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace LearnSmartManufacturing.Models.SerialTcp
{
    public enum FramingProtocolKind
    {
        BinaryHeaderLengthCrc, // [0xAA 0x55][Cmd:1][Seq:1][Len:1][Payload:N][CRC16:2]
        AsciiDelimiterCrLf,    // ASCII payload terminated by \r\n (0x0D 0x0A)
        FixedLength12Byte,     // Fixed 12B: [0xFE][Id:1][Val1:4][Val2:4][Sum8:1][0xEF]
    }

    public enum LinkTransportKind
    {
        SimulatedByteStream,
        RealLoopbackTcpSocket,
    }

    public enum ReconnectLinkState
    {
        Connected,
        HeartbeatTimeout,
        BackoffReconnecting,
        Disconnected,
    }

    public sealed record StreamChunkRecord(int ChunkId, DateTime Timestamp, byte[] Bytes, string Label, string Kind)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
        public string HexText => CustomProtocolCodec.ToHex(Bytes, maxBytes: 22);
    }

    public sealed record DecodedFrameRecord(
        int FrameId,
        DateTime Timestamp,
        FramingProtocolKind Protocol,
        string CommandTag,
        byte Sequence,
        string PayloadText,
        string RawHex,
        bool IsValid,
        string StatusText)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
    }

    public sealed record FramingPipelineSnapshot(
        FramingProtocolKind Protocol,
        IReadOnlyList<StreamChunkRecord> RecentChunks,
        byte[] PendingBufferBytes,
        IReadOnlyList<DecodedFrameRecord> RecentFrames,
        int ValidFrameCount,
        int CrcErrorCount,
        int GarbageBytesDiscarded);

    public sealed record Rs485BusEvent(
        long EventId,
        DateTime Timestamp,
        string DeviceName,
        string Direction, // TX, RX, COLLISION, HEARTBEAT, RECONNECT
        string Detail,
        double LatencyMs,
        bool IsError)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
    }

    public sealed record SerialTimingResult(
        int BaudRate,
        string FormatLabel,
        double BitsPerByte,
        double ByteTimeMs,
        double FrameTimeMs,
        double T35SilentMs,
        double MaxPollHz,
        string Summary);

    /// <summary>
    /// Encoders for the three classic industrial byte-stream framing protocols:
    /// 1) Binary Header + Length + CRC-16, 2) ASCII \r\n Delimiter, 3) Fixed 12-Byte Frame.
    /// </summary>
    public static class CustomProtocolCodec
    {
        public const byte Header0 = 0xAA;
        public const byte Header1 = 0x55;
        public const int BinaryMinFrameLength = 7; // 2(Hdr) + 1(Cmd) + 1(Seq) + 1(Len) + 0(Payload) + 2(CRC)
        public const int FixedFrameLength = 12;

        public static byte[] BuildBinaryFrame(byte cmd, byte seq, string payloadUtf8)
        {
            var payload = Encoding.UTF8.GetBytes(payloadUtf8);
            if (payload.Length > 200)
            {
                payload = payload[..200];
            }

            var frame = new byte[5 + payload.Length + 2];
            frame[0] = Header0;
            frame[1] = Header1;
            frame[2] = cmd;
            frame[3] = seq;
            frame[4] = (byte)payload.Length;
            payload.CopyTo(frame, 5);

            var crc = ComputeCrc16Modbus(frame.AsSpan(0, 5 + payload.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5 + payload.Length, 2), crc);
            return frame;
        }

        public static byte[] BuildAsciiFrame(string text)
        {
            var clean = text.Replace("\r", string.Empty).Replace("\n", string.Empty);
            return Encoding.ASCII.GetBytes(clean + "\r\n");
        }

        public static byte[] BuildFixed12Frame(byte deviceId, float value1, float value2)
        {
            var frame = new byte[FixedFrameLength];
            frame[0] = 0xFE;
            frame[1] = deviceId;
            BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(2, 4), value1);
            BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(6, 4), value2);
            byte sum = 0;
            for (var i = 1; i <= 9; i++)
            {
                sum += frame[i];
            }

            frame[10] = sum;
            frame[11] = 0xEF;
            return frame;
        }

        public static byte[] CorruptLastByte(byte[] frame)
        {
            if (frame.Length == 0) return [];
            var copy = frame.ToArray();
            copy[^1] ^= 0xFF;
            return copy;
        }

        public static ushort ComputeCrc16Modbus(ReadOnlySpan<byte> data)
        {
            ushort crc = 0xFFFF;
            foreach (var b in data)
            {
                crc ^= b;
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            return crc;
        }

        public static string ToHex(ReadOnlySpan<byte> bytes, int maxBytes = 24)
        {
            if (bytes.Length == 0) return string.Empty;
            var count = Math.Min(bytes.Length, maxBytes);
            var hex = string.Join(' ', bytes[..count].ToArray().Select(b => b.ToString("X2")));
            return bytes.Length > maxBytes ? $"{hex} … ({bytes.Length}B)" : hex;
        }
    }

    /// <summary>
    /// Zero-dependency incremental byte-stream frame parser (PipeReader / ReadOnlySpan sliding-buffer semantics)
    /// handling TCP sticky packets, arbitrary half-packet fragmentation, leading EMI noise resynchronization, and CRC checks.
    /// </summary>
    public sealed class StreamFrameDecoder
    {
        private readonly List<byte> _rxBuffer = new(256);
        private int _nextFrameId = 1;

        public FramingProtocolKind Protocol { get; private set; } = FramingProtocolKind.BinaryHeaderLengthCrc;
        public int ValidFrameCount { get; private set; }
        public int CrcErrorCount { get; private set; }
        public int GarbageBytesDiscarded { get; private set; }

        public byte[] GetPendingBufferCopy() => [.. _rxBuffer];

        public void SetProtocol(FramingProtocolKind protocol)
        {
            Protocol = protocol;
            Reset();
        }

        public void Reset()
        {
            _rxBuffer.Clear();
            ValidFrameCount = 0;
            CrcErrorCount = 0;
            GarbageBytesDiscarded = 0;
            _nextFrameId = 1;
        }

        /// <summary>Feeds an arbitrary socket/serial byte chunk and returns all frames decoded from the accumulated stream.</summary>
        public IReadOnlyList<DecodedFrameRecord> Feed(ReadOnlySpan<byte> chunk)
        {
            for (var i = 0; i < chunk.Length; i++)
            {
                _rxBuffer.Add(chunk[i]);
            }

            var output = new List<DecodedFrameRecord>();
            switch (Protocol)
            {
                case FramingProtocolKind.BinaryHeaderLengthCrc:
                    DecodeBinaryFrames(output);
                    break;
                case FramingProtocolKind.AsciiDelimiterCrLf:
                    DecodeAsciiFrames(output);
                    break;
                case FramingProtocolKind.FixedLength12Byte:
                    DecodeFixed12Frames(output);
                    break;
            }

            return output;
        }

        private void DecodeBinaryFrames(List<DecodedFrameRecord> output)
        {
            while (true)
            {
                // 1. Slide forward until 2-byte sync header 0xAA 0x55 is found
                var syncIdx = -1;
                for (var i = 0; i < _rxBuffer.Count - 1; i++)
                {
                    if (_rxBuffer[i] == CustomProtocolCodec.Header0 && _rxBuffer[i + 1] == CustomProtocolCodec.Header1)
                    {
                        syncIdx = i;
                        break;
                    }
                }

                if (syncIdx < 0)
                {
                    // Keep last byte only if it might be the first byte (0xAA) of a split sync header
                    var keepLast = _rxBuffer.Count > 0 && _rxBuffer[^1] == CustomProtocolCodec.Header0 ? 1 : 0;
                    var discard = _rxBuffer.Count - keepLast;
                    if (discard > 0)
                    {
                        _rxBuffer.RemoveRange(0, discard);
                        GarbageBytesDiscarded += discard;
                    }

                    return;
                }

                if (syncIdx > 0)
                {
                    _rxBuffer.RemoveRange(0, syncIdx);
                    GarbageBytesDiscarded += syncIdx;
                }

                // 2. Need at least 5 bytes to read payload length
                if (_rxBuffer.Count < 5)
                {
                    return;
                }

                var payloadLen = _rxBuffer[4];
                if (payloadLen > 200)
                {
                    // Implausible length -> false header sync, advance by 2 bytes and resync
                    _rxBuffer.RemoveRange(0, 2);
                    GarbageBytesDiscarded += 2;
                    continue;
                }

                var totalLen = 5 + payloadLen + 2;
                if (_rxBuffer.Count < totalLen)
                {
                    // Half-packet: wait for remaining bytes to arrive in next chunk!
                    return;
                }

                // 3. Extract candidate frame and verify CRC-16
                var raw = _rxBuffer.GetRange(0, totalLen).ToArray();
                var expectedCrc = CustomProtocolCodec.ComputeCrc16Modbus(raw.AsSpan(0, 5 + payloadLen));
                var actualCrc = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(5 + payloadLen, 2));

                if (expectedCrc != actualCrc)
                {
                    CrcErrorCount++;
                    output.Add(new DecodedFrameRecord(
                        _nextFrameId++, DateTime.Now, Protocol, $"Cmd 0x{raw[2]:X2}", raw[3],
                        $"CRC 校验失败 (实收 0x{actualCrc:X4} ≠ 计算 0x{expectedCrc:X4})",
                        CustomProtocolCodec.ToHex(raw, 20), IsValid: false, StatusText: "CRC ERR"));
                    // Remove full corrupt frame (or slide past header); removing totalLen prevents false re-parse of payload
                    _rxBuffer.RemoveRange(0, totalLen);
                    continue;
                }

                ValidFrameCount++;
                var payloadStr = Encoding.UTF8.GetString(raw, 5, payloadLen);
                output.Add(new DecodedFrameRecord(
                    _nextFrameId++, DateTime.Now, Protocol, $"Cmd 0x{raw[2]:X2}", raw[3],
                    payloadStr, CustomProtocolCodec.ToHex(raw, 20), IsValid: true, StatusText: "CRC OK"));
                _rxBuffer.RemoveRange(0, totalLen);
            }
        }

        private void DecodeAsciiFrames(List<DecodedFrameRecord> output)
        {
            while (true)
            {
                var crlfIdx = -1;
                for (var i = 0; i < _rxBuffer.Count - 1; i++)
                {
                    if (_rxBuffer[i] == 0x0D && _rxBuffer[i + 1] == 0x0A)
                    {
                        crlfIdx = i;
                        break;
                    }
                }

                if (crlfIdx < 0)
                {
                    return; // Half-line waiting for \r\n
                }

                var rawLine = _rxBuffer.GetRange(0, crlfIdx + 2).ToArray();
                _rxBuffer.RemoveRange(0, crlfIdx + 2);

                // Trim any leading non-printable EMI bytes before ASCII text
                var start = 0;
                while (start < crlfIdx && (rawLine[start] < 0x20 || rawLine[start] > 0x7E))
                {
                    start++;
                }

                if (start > 0)
                {
                    GarbageBytesDiscarded += start;
                }

                var text = Encoding.ASCII.GetString(rawLine, start, crlfIdx - start);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                ValidFrameCount++;
                output.Add(new DecodedFrameRecord(
                    _nextFrameId++, DateTime.Now, Protocol, "ASCII", (byte)(ValidFrameCount & 0xFF),
                    text, CustomProtocolCodec.ToHex(rawLine, 20), IsValid: true, StatusText: "\\r\\n OK"));
            }
        }

        private void DecodeFixed12Frames(List<DecodedFrameRecord> output)
        {
            while (true)
            {
                var startIdx = _rxBuffer.IndexOf(0xFE);
                if (startIdx < 0)
                {
                    if (_rxBuffer.Count > 0)
                    {
                        GarbageBytesDiscarded += _rxBuffer.Count;
                        _rxBuffer.Clear();
                    }

                    return;
                }

                if (startIdx > 0)
                {
                    _rxBuffer.RemoveRange(0, startIdx);
                    GarbageBytesDiscarded += startIdx;
                }

                if (_rxBuffer.Count < CustomProtocolCodec.FixedFrameLength)
                {
                    return; // Wait for all 12 bytes
                }

                var raw = _rxBuffer.GetRange(0, CustomProtocolCodec.FixedFrameLength).ToArray();
                if (raw[11] != 0xEF)
                {
                    // Invalid tail marker -> false start byte, drop 1 byte and resync
                    _rxBuffer.RemoveAt(0);
                    GarbageBytesDiscarded++;
                    continue;
                }

                byte expectedSum = 0;
                for (var i = 1; i <= 9; i++) expectedSum += raw[i];
                if (expectedSum != raw[10])
                {
                    CrcErrorCount++;
                    output.Add(new DecodedFrameRecord(
                        _nextFrameId++, DateTime.Now, Protocol, $"ID {raw[1]}", raw[1],
                        $"Sum8 校验失败 (实收 0x{raw[10]:X2} ≠ 计算 0x{expectedSum:X2})",
                        CustomProtocolCodec.ToHex(raw, 12), IsValid: false, StatusText: "SUM ERR"));
                    _rxBuffer.RemoveRange(0, CustomProtocolCodec.FixedFrameLength);
                    continue;
                }

                var v1 = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(2, 4));
                var v2 = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(6, 4));
                ValidFrameCount++;
                output.Add(new DecodedFrameRecord(
                    _nextFrameId++, DateTime.Now, Protocol, $"ID {raw[1]}", raw[1],
                    string.Create(CultureInfo.InvariantCulture, $"Dist={v1:F2} mm, Temp={v2:F1} °C"),
                    CustomProtocolCodec.ToHex(raw, 12), IsValid: true, StatusText: "12B OK"));
                _rxBuffer.RemoveRange(0, CustomProtocolCodec.FixedFrameLength);
            }
        }
    }

    /// <summary>Computes RS232/RS485 wire serialization times, Modbus/RS485 T3.5 silent intervals, and max polling rates.</summary>
    public static class SerialTimingCalculator
    {
        public static SerialTimingResult Calculate(
            int baudRate,
            int dataBits,
            bool hasParity,
            double stopBits,
            int txBytes,
            int rxBytes,
            double turnaroundMs)
        {
            var baud = Math.Max(1200, baudRate);
            var bitsPerByte = 1.0 + dataBits + (hasParity ? 1.0 : 0.0) + stopBits;
            var byteTimeMs = (bitsPerByte / baud) * 1000.0;
            var totalBytes = Math.Max(1, txBytes + rxBytes);
            var frameTimeMs = totalBytes * byteTimeMs;

            // Per Modbus Serial specification: T3.5 = 3.5 char times for <=19200 bps, fixed 1.75 ms for >19200 bps
            var t35Ms = baud > 19200 ? 1.75 : 3.5 * byteTimeMs;
            var roundTripMs = frameTimeMs + Math.Max(0, turnaroundMs) + t35Ms * 2.0;
            var maxHz = 1000.0 / Math.Max(0.1, roundTripMs);

            var fmt = $"{dataBits}{(hasParity ? "E" : "N")}{stopBits:0.#}";
            var summary = string.Create(CultureInfo.InvariantCulture,
                $"{baud} bps ({fmt}, {bitsPerByte:0.#} bit/B)：单字节 {byteTimeMs:F3} ms，收发 {totalBytes} B + 转向 {turnaroundMs:F1} ms → 理论最高轮询 {maxHz:F1} Hz。");

            return new SerialTimingResult(
                baud, fmt, bitsPerByte,
                Math.Round(byteTimeMs, 4),
                Math.Round(frameTimeMs, 3),
                Math.Round(t35Ms, 3),
                Math.Round(maxHz, 1),
                summary);
        }
    }
}
