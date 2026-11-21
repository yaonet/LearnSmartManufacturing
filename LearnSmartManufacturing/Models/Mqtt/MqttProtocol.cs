using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace LearnSmartManufacturing.Models.Mqtt
{
    public enum MqttTransportMode
    {
        SimulatedBroker,
        RealTcpBroker,
    }

    public enum MqttQos : byte
    {
        AtMostOnce = 0,
        AtLeastOnce = 1,
        ExactlyOnce = 2,
    }

    public enum MqttConnectionState
    {
        Disconnected,
        Connected,
        NetworkLost,
    }

    public sealed record MqttWillConfig(string Topic, string Payload, MqttQos Qos, bool Retain);

    public sealed record MqttHandshakeStep(
        string PacketType,
        bool FromClientToBroker,
        string Detail,
        bool IsLost = false,
        bool IsRetransmit = false,
        bool IsLwt = false);

    public sealed record MqttHandshakeTrace(
        DateTime Timestamp,
        string Title,
        MqttQos Qos,
        byte[] RawPacketBytes,
        IReadOnlyList<MqttHandshakeStep> Steps);

    public sealed record MqttReceivedMessage(
        DateTime Timestamp,
        string Topic,
        string Payload,
        MqttQos Qos,
        bool IsRetained,
        bool IsLwt,
        bool IsDuplicate,
        bool IsReplayed,
        string MatchedFilter)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
        public string QosTag => $"QoS {(int)Qos}";

        public string FlagsText
        {
            get
            {
                var parts = new List<string>();
                if (IsRetained) parts.Add("RETAIN");
                if (IsLwt) parts.Add("LWT");
                if (IsDuplicate) parts.Add("DUP");
                if (IsReplayed) parts.Add("断网续传");
                return parts.Count > 0 ? string.Join(" · ", parts) : "LIVE";
            }
        }
    }

    public sealed record MqttRetainedEntry(string Topic, string Payload, MqttQos Qos, DateTime UpdatedAt)
    {
        public string UpdatedText => UpdatedAt.ToString("HH:mm:ss");
        public string QosTag => $"QoS {(int)Qos}";
    }

    public sealed record MqttSubscriptionEntry(string Filter, MqttQos MaxQos, int HitCount)
    {
        public string QosTag => $"QoS {(int)MaxQos}";
    }

    public sealed record TopicMatchTestRow(string SampleTopic, bool IsMatch, string Reason);

    public sealed record MqttProtocolLog(DateTime Timestamp, string Level, string Packet, string Message)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
    }

    /// <summary>MQTT 3.1.1 specification-compliant topic filter validator and wildcard matcher (+ and #).</summary>
    public static class MqttTopicMatcher
    {
        public static bool IsValidTopicName(string? topic, out string error)
        {
            if (string.IsNullOrEmpty(topic))
            {
                error = "发布主题不能为空。";
                return false;
            }

            if (topic.Contains('+') || topic.Contains('#'))
            {
                error = "发布主题名中不能包含通配符 '+' 或 '#'。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static bool IsValidTopicFilter(string? filter, out string error)
        {
            if (string.IsNullOrEmpty(filter))
            {
                error = "订阅过滤器不能为空。";
                return false;
            }

            var levels = filter.Split('/');
            for (var i = 0; i < levels.Length; i++)
            {
                var level = levels[i];
                if (level.Contains('#'))
                {
                    if (level != "#" || i != levels.Length - 1)
                    {
                        error = "多层通配符 '#' 必须单独占据最后一级（例如 factory/line1/#）。";
                        return false;
                    }
                }

                if (level.Contains('+') && level != "+")
                {
                    error = "单层通配符 '+' 必须单独占据整级（不能与其它字符混写）。";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        public static bool IsMatch(string filter, string topic)
        {
            if (!IsValidTopicFilter(filter, out _) || !IsValidTopicName(topic, out _))
            {
                return false;
            }

            // MQTT 3.1.1 §4.7.2: Topics beginning with '$' do not match leading '#' or '+' wildcards.
            if (topic.StartsWith('$') && (filter.StartsWith('#') || filter.StartsWith('+')))
            {
                return false;
            }

            var filterLevels = filter.Split('/');
            var topicLevels = topic.Split('/');

            for (var i = 0; i < filterLevels.Length; i++)
            {
                var f = filterLevels[i];
                if (f == "#")
                {
                    return true;
                }

                if (i >= topicLevels.Length)
                {
                    return false;
                }

                if (f != "+" && !string.Equals(f, topicLevels[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            // "a/b/#" also matches parent topic "a/b" (handled when filter has 1 trailing '#' after matched prefix).
            if (filterLevels.Length == topicLevels.Length)
            {
                return true;
            }

            return false;
        }

        public static bool IsMatchWithParentMultiLevel(string filter, string topic)
        {
            if (IsMatch(filter, topic))
            {
                return true;
            }

            // MQTT 3.1.1 §4.7.1.2: "sport/tennis/player1/#" matches "sport/tennis/player1".
            if (filter.EndsWith("/#", StringComparison.Ordinal))
            {
                var parent = filter[..^2];
                return IsMatch(parent, topic);
            }

            return false;
        }
    }

    /// <summary>MQTT 3.1.1 binary control packet encoder and variable-byte-integer codec.</summary>
    public static class MqttPacketCodec
    {
        public static byte[] EncodeRemainingLength(int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            if (length > 268_435_455)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Remaining length exceeds MQTT 3.1.1 limit.");
            }

            var bytes = new List<byte>(4);
            var x = length;
            do
            {
                var encodedByte = (byte)(x % 128);
                x /= 128;
                if (x > 0)
                {
                    encodedByte |= 0x80;
                }

                bytes.Add(encodedByte);
            }
            while (x > 0);

            return [.. bytes];
        }

        public static bool TryDecodeRemainingLength(ReadOnlySpan<byte> buffer, out int value, out int bytesConsumed)
        {
            value = 0;
            bytesConsumed = 0;
            var multiplier = 1;

            for (var i = 0; i < Math.Min(4, buffer.Length); i++)
            {
                var encodedByte = buffer[i];
                value += (encodedByte & 0x7F) * multiplier;
                bytesConsumed++;
                if ((encodedByte & 0x80) == 0)
                {
                    return true;
                }

                multiplier *= 128;
            }

            return false;
        }

        public static byte[] BuildConnectPacket(string clientId, ushort keepAliveSec, MqttWillConfig? will = null)
        {
            using var body = new MemoryStream();
            WriteMqttString(body, "MQTT");
            body.WriteByte(0x04); // MQTT 3.1.1

            byte flags = 0x02; // CleanSession
            if (will is not null && !string.IsNullOrEmpty(will.Topic))
            {
                flags |= 0x04; // Will Flag
                flags |= (byte)(((byte)will.Qos & 0x03) << 3);
                if (will.Retain)
                {
                    flags |= 0x20;
                }
            }

            body.WriteByte(flags);
            WriteUInt16(body, keepAliveSec);
            WriteMqttString(body, clientId);
            if (will is not null && !string.IsNullOrEmpty(will.Topic))
            {
                WriteMqttString(body, will.Topic);
                WriteMqttString(body, will.Payload);
            }

            return BuildPacket(0x10, body.ToArray());
        }

        public static byte[] BuildPublishPacket(string topic, string payloadUtf8, MqttQos qos, bool retain, bool dup = false, ushort packetId = 1)
        {
            using var body = new MemoryStream();
            WriteMqttString(body, topic);
            if (qos != MqttQos.AtMostOnce)
            {
                WriteUInt16(body, packetId);
            }

            var payloadBytes = Encoding.UTF8.GetBytes(payloadUtf8);
            body.Write(payloadBytes);

            byte header = 0x30;
            if (dup && qos != MqttQos.AtMostOnce) header |= 0x08;
            header |= (byte)(((byte)qos & 0x03) << 1);
            if (retain) header |= 0x01;

            return BuildPacket(header, body.ToArray());
        }

        public static byte[] BuildSubscribePacket(ushort packetId, string topicFilter, MqttQos qos)
        {
            using var body = new MemoryStream();
            WriteUInt16(body, packetId);
            WriteMqttString(body, topicFilter);
            body.WriteByte((byte)qos);
            return BuildPacket(0x82, body.ToArray());
        }

        public static byte[] BuildUnsubscribePacket(ushort packetId, string topicFilter)
        {
            using var body = new MemoryStream();
            WriteUInt16(body, packetId);
            WriteMqttString(body, topicFilter);
            return BuildPacket(0xA2, body.ToArray());
        }

        public static byte[] BuildPacketIdAck(byte fixedHeader, ushort packetId)
        {
            var buf = new byte[4];
            buf[0] = fixedHeader;
            buf[1] = 0x02;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2, 2), packetId);
            return buf;
        }

        public static byte[] BuildPubAckPacket(ushort packetId) => BuildPacketIdAck(0x40, packetId);
        public static byte[] BuildPubRecPacket(ushort packetId) => BuildPacketIdAck(0x50, packetId);
        public static byte[] BuildPubRelPacket(ushort packetId) => BuildPacketIdAck(0x62, packetId);
        public static byte[] BuildPubCompPacket(ushort packetId) => BuildPacketIdAck(0x70, packetId);
        public static byte[] BuildPingReqPacket() => [0xC0, 0x00];
        public static byte[] BuildDisconnectPacket() => [0xE0, 0x00];

        public static bool TryParsePublishPacket(
            byte fixedHeader,
            ReadOnlySpan<byte> body,
            out string topic,
            out string payload,
            out MqttQos qos,
            out bool retain,
            out bool dup,
            out ushort packetId)
        {
            topic = string.Empty;
            payload = string.Empty;
            dup = (fixedHeader & 0x08) != 0;
            qos = (MqttQos)((fixedHeader >> 1) & 0x03);
            retain = (fixedHeader & 0x01) != 0;
            packetId = 0;

            if (body.Length < 2)
            {
                return false;
            }

            var topicLen = BinaryPrimitives.ReadUInt16BigEndian(body[0..2]);
            var offset = 2 + topicLen;
            if (body.Length < offset)
            {
                return false;
            }

            topic = Encoding.UTF8.GetString(body.Slice(2, topicLen));
            if (qos != MqttQos.AtMostOnce)
            {
                if (body.Length < offset + 2)
                {
                    return false;
                }

                packetId = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(offset, 2));
                offset += 2;
            }

            payload = Encoding.UTF8.GetString(body[offset..]);
            return true;
        }

        public static async Task<(byte Header, byte[] Body)?> ReadPacketAsync(Stream stream, CancellationToken ct)
        {
            var first = new byte[1];
            var read = await stream.ReadAsync(first, ct).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            var header = first[0];
            var multiplier = 1;
            var remainingLength = 0;
            var single = new byte[1];
            for (var i = 0; i < 4; i++)
            {
                await stream.ReadExactlyAsync(single, ct).ConfigureAwait(false);
                var b = single[0];
                remainingLength += (b & 0x7F) * multiplier;
                if ((b & 0x80) == 0)
                {
                    break;
                }

                multiplier *= 128;
            }

            var body = new byte[remainingLength];
            if (remainingLength > 0)
            {
                await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
            }

            return (header, body);
        }

        public static string ToHexString(ReadOnlySpan<byte> bytes, int maxBytes = 24)
        {
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            var count = Math.Min(bytes.Length, maxBytes);
            var hex = string.Join(' ', bytes[..count].ToArray().Select(b => b.ToString("X2")));
            return bytes.Length > maxBytes ? $"{hex} … ({bytes.Length}B)" : hex;
        }

        private static byte[] BuildPacket(byte fixedHeader, byte[] body)
        {
            var rem = EncodeRemainingLength(body.Length);
            var packet = new byte[1 + rem.Length + body.Length];
            packet[0] = fixedHeader;
            rem.CopyTo(packet, 1);
            body.CopyTo(packet, 1 + rem.Length);
            return packet;
        }

        private static void WriteUInt16(Stream s, ushort value)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, value);
            s.Write(b);
        }

        private static void WriteMqttString(Stream s, string text)
        {
            var utf8 = Encoding.UTF8.GetBytes(text);
            WriteUInt16(s, (ushort)utf8.Length);
            s.Write(utf8);
        }
    }
}
