using System.IO;
using LearnSmartManufacturing.Models.Mqtt;
using Xunit;

namespace LearnSmartManufacturing.Tests.Models
{
    public class MqttProtocolTests
    {
        [Theory]
        [InlineData("factory/line1/+/telemetry", "factory/line1/smt-01/telemetry", true)]
        [InlineData("factory/line1/+/telemetry", "factory/line1/aoi-02/telemetry", true)]
        [InlineData("factory/line1/+/telemetry", "factory/line1/smt-01/status", false)]
        [InlineData("factory/line1/+/telemetry", "factory/line1/smt-01/cmd/telemetry", false)]
        [InlineData("factory/line1/#", "factory/line1/smt-01/telemetry", true)]
        [InlineData("factory/line1/#", "factory/line1/smt-01/cmd/recipe", true)]
        [InlineData("factory/line1/#", "factory/line1", true)] // MQTT 3.1.1 §4.7.1.2 parent match
        [InlineData("#", "factory/line2/cnc-01/status", true)]
        [InlineData("#", "$SYS/broker/clients/connected", false)] // MQTT 3.1.1 §4.7.2 $ exclusion
        [InlineData("+/broker/#", "$SYS/broker/clients/connected", false)]
        [InlineData("$SYS/#", "$SYS/broker/clients/connected", true)]
        public void TopicMatcher_FollowsMqtt311WildcardSpec(string filter, string topic, bool expected)
        {
            Assert.Equal(expected, MqttTopicMatcher.IsMatchWithParentMultiLevel(filter, topic));
        }

        [Theory]
        [InlineData("", false)]
        [InlineData("factory/#/telemetry", false)]
        [InlineData("factory/line1#", false)]
        [InlineData("factory/line+/telemetry", false)]
        [InlineData("factory/+/+/status", true)]
        [InlineData("factory/line1/#", true)]
        public void TopicFilterValidation_RejectsMalformedWildcards(string filter, bool expectedValid)
        {
            Assert.Equal(expectedValid, MqttTopicMatcher.IsValidTopicFilter(filter, out _));
        }

        [Theory]
        [InlineData("factory/line1/smt-01/telemetry", true)]
        [InlineData("factory/line1/+/telemetry", false)]
        [InlineData("factory/line1/#", false)]
        [InlineData("", false)]
        public void TopicNameValidation_RejectsWildcardsAndEmpty(string topic, bool expectedValid)
        {
            Assert.Equal(expectedValid, MqttTopicMatcher.IsValidTopicName(topic, out _));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(127, 1)]
        [InlineData(128, 2)]
        [InlineData(16_383, 2)]
        [InlineData(16_384, 3)]
        [InlineData(2_097_151, 3)]
        [InlineData(2_097_152, 4)]
        [InlineData(268_435_455, 4)]
        public void RemainingLength_VariableByteInteger_RoundTrips(int length, int expectedBytes)
        {
            var encoded = MqttPacketCodec.EncodeRemainingLength(length);
            Assert.Equal(expectedBytes, encoded.Length);

            var ok = MqttPacketCodec.TryDecodeRemainingLength(encoded, out var decoded, out var consumed);
            Assert.True(ok);
            Assert.Equal(length, decoded);
            Assert.Equal(expectedBytes, consumed);
        }

        [Fact]
        public void RemainingLength_KnownSpecVectors_MatchExactly()
        {
            Assert.Equal(new byte[] { 0x80, 0x01 }, MqttPacketCodec.EncodeRemainingLength(128));
            Assert.Equal(new byte[] { 0xFF, 0x7F }, MqttPacketCodec.EncodeRemainingLength(16_383));
        }

        [Fact]
        public async Task BuildAndReadPublishPacket_RoundTripsAllQosLevelsAndFlags()
        {
            var packet = MqttPacketCodec.BuildPublishPacket(
                topic: "factory/line1/smt-01/alarm",
                payloadUtf8: "{\"code\":\"E402\"}",
                qos: MqttQos.AtLeastOnce,
                retain: true,
                dup: true,
                packetId: 42);

            await using var ms = new MemoryStream(packet);
            var parsed = await MqttPacketCodec.ReadPacketAsync(ms, CancellationToken.None);
            Assert.NotNull(parsed);

            var ok = MqttPacketCodec.TryParsePublishPacket(
                parsed.Value.Header, parsed.Value.Body,
                out var topic, out var payload, out var qos, out var retain, out var dup, out var packetId);

            Assert.True(ok);
            Assert.Equal("factory/line1/smt-01/alarm", topic);
            Assert.Equal("{\"code\":\"E402\"}", payload);
            Assert.Equal(MqttQos.AtLeastOnce, qos);
            Assert.True(retain);
            Assert.True(dup);
            Assert.Equal((ushort)42, packetId);
        }

        [Fact]
        public void BuildConnectPacket_EncodesProtocolAndWillFlags()
        {
            var will = new MqttWillConfig("factory/line1/smt-01/status", "{\"online\":false}", MqttQos.AtLeastOnce, Retain: true);
            var packet = MqttPacketCodec.BuildConnectPacket("edge-gw-01", keepAliveSec: 30, will);

            Assert.Equal(0x10, packet[0]); // CONNECT
            // Protocol string length (0x00, 0x04) + "MQTT" + Level 0x04 + Flags
            var ok = MqttPacketCodec.TryDecodeRemainingLength(packet.AsSpan(1), out var remLen, out var remBytes);
            Assert.True(ok);
            Assert.Equal(packet.Length - 1 - remBytes, remLen);

            var bodyStart = 1 + remBytes;
            Assert.Equal(0x04, packet[bodyStart + 6]); // MQTT 3.1.1 level
            var connectFlags = packet[bodyStart + 7];
            Assert.Equal(0x2E, connectFlags); // CleanSession(0x02) | WillFlag(0x04) | WillQoS1(0x08) | WillRetain(0x20)
        }

        [Fact]
        public void ControlAckPackets_UseSpecCompliantFixedHeaders()
        {
            Assert.Equal(new byte[] { 0x40, 0x02, 0x00, 0x0A }, MqttPacketCodec.BuildPubAckPacket(10));
            Assert.Equal(new byte[] { 0x50, 0x02, 0x00, 0x0A }, MqttPacketCodec.BuildPubRecPacket(10));
            Assert.Equal(new byte[] { 0x62, 0x02, 0x00, 0x0A }, MqttPacketCodec.BuildPubRelPacket(10));
            Assert.Equal(new byte[] { 0x70, 0x02, 0x00, 0x0A }, MqttPacketCodec.BuildPubCompPacket(10));
            Assert.Equal(new byte[] { 0xE0, 0x00 }, MqttPacketCodec.BuildDisconnectPacket());
        }
    }
}
