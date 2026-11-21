using LearnSmartManufacturing.Models.SerialTcp;
using LearnSmartManufacturing.Services.SerialTcp;
using Xunit;

namespace LearnSmartManufacturing.Tests.Models
{
    public class SerialTcpProtocolTests
    {
        [Fact]
        public void BinaryDecoder_StickyPacket_ExtractsAllThreeFramesInSingleFeed()
        {
            var decoder = new StreamFrameDecoder();
            var f1 = CustomProtocolCodec.BuildBinaryFrame(0x01, 1, "A1");
            var f2 = CustomProtocolCodec.BuildBinaryFrame(0x01, 2, "B2");
            var f3 = CustomProtocolCodec.BuildBinaryFrame(0x01, 3, "C3");
            byte[] merged = [.. f1, .. f2, .. f3];

            var frames = decoder.Feed(merged);

            Assert.Equal(3, frames.Count);
            Assert.All(frames, f => Assert.True(f.IsValid));
            Assert.Equal(["A1", "B2", "C3"], frames.Select(f => f.PayloadText).ToArray());
            Assert.Empty(decoder.GetPendingBufferCopy());
        }

        [Fact]
        public void BinaryDecoder_ByteByByteHalfPacketAndSplitSyncHeader_ReassemblesCleanly()
        {
            var decoder = new StreamFrameDecoder();
            var frame = CustomProtocolCodec.BuildBinaryFrame(0x02, 7, "SPLIT-TEST");
            byte[] stream = [0x99, 0x00, .. frame]; // 2 garbage bytes + full frame (including 0xAA / 0x55 split boundary)

            var allDecoded = new List<DecodedFrameRecord>();
            for (var i = 0; i < stream.Length; i++)
            {
                allDecoded.AddRange(decoder.Feed(stream.AsSpan(i, 1)));
            }

            Assert.Single(allDecoded);
            Assert.True(allDecoded[0].IsValid);
            Assert.Equal("SPLIT-TEST", allDecoded[0].PayloadText);
            Assert.Equal(2, decoder.GarbageBytesDiscarded);
            Assert.Empty(decoder.GetPendingBufferCopy());
        }

        [Fact]
        public void BinaryDecoder_EmiNoiseAndCorruptCrc_RejectsBadFrameAndRecoversNext()
        {
            var decoder = new StreamFrameDecoder();
            byte[] noise = [0x13, 0xFF, 0x00];
            var bad = CustomProtocolCodec.CorruptLastByte(CustomProtocolCodec.BuildBinaryFrame(0x01, 1, "BAD"));
            var good = CustomProtocolCodec.BuildBinaryFrame(0x01, 2, "GOOD");

            var decoded = decoder.Feed([.. noise, .. bad, .. good]);

            Assert.Equal(2, decoded.Count);
            Assert.False(decoded[0].IsValid);
            Assert.True(decoded[1].IsValid);
            Assert.Equal("GOOD", decoded[1].PayloadText);
            Assert.Equal(3, decoder.GarbageBytesDiscarded);
            Assert.Equal(1, decoder.CrcErrorCount);
            Assert.Equal(1, decoder.ValidFrameCount);
        }

        [Fact]
        public void AsciiDelimiterDecoder_HandlesStickyAndSplitLines()
        {
            var decoder = new StreamFrameDecoder();
            decoder.SetProtocol(FramingProtocolKind.AsciiDelimiterCrLf);

            var part1 = System.Text.Encoding.ASCII.GetBytes("SN:001\r\nSN:00");
            var part2 = System.Text.Encoding.ASCII.GetBytes("2\r\n");

            var out1 = decoder.Feed(part1);
            Assert.Single(out1);
            Assert.Equal("SN:001", out1[0].PayloadText);
            Assert.NotEmpty(decoder.GetPendingBufferCopy());

            var out2 = decoder.Feed(part2);
            Assert.Single(out2);
            Assert.Equal("SN:002", out2[0].PayloadText);
            Assert.Empty(decoder.GetPendingBufferCopy());
        }

        [Fact]
        public void Fixed12Decoder_ResyncsPastNoiseAndValidatesChecksum()
        {
            var decoder = new StreamFrameDecoder();
            decoder.SetProtocol(FramingProtocolKind.FixedLength12Byte);

            var f = CustomProtocolCodec.BuildFixed12Frame(deviceId: 5, value1: 12.5f, value2: 40.0f);
            var outFrames = decoder.Feed([0x10, 0x20, .. f]);

            Assert.Single(outFrames);
            Assert.True(outFrames[0].IsValid);
            Assert.Contains("12.50", outFrames[0].PayloadText);
            Assert.Equal(2, decoder.GarbageBytesDiscarded);
        }

        [Fact]
        public void SerialTimingCalculator_AppliesModbusT35ClampAbove19200()
        {
            var slow = SerialTimingCalculator.Calculate(9600, 8, false, 1.0, 8, 16, 2.0);
            var fast = SerialTimingCalculator.Calculate(115200, 8, false, 1.0, 8, 16, 2.0);

            Assert.Equal(10.0, slow.BitsPerByte);
            Assert.InRange(slow.T35SilentMs, 3.6, 3.7);
            Assert.Equal(1.75, fast.T35SilentMs);
            Assert.True(fast.MaxPollHz > slow.MaxPollHz * 3.0);
        }

        [Fact]
        public async Task LoopbackTcpChannel_TransfersRealSocketBytesAndDisposesCleanly()
        {
            using var channel = new LoopbackTcpChannel();
            var tcs = new TaskCompletionSource<byte[]>();
            channel.ChunkReceived += bytes => tcs.TrySetResult(bytes);

            await channel.StartAsync();
            Assert.True(channel.IsConnected);
            Assert.True(channel.Port > 0);

            var frame = CustomProtocolCodec.BuildBinaryFrame(0x05, 1, "REAL-TCP");
            await channel.SendAsync(frame);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(frame, received);
        }
    }
}
