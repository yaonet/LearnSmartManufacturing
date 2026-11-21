using LearnSmartManufacturing.Models.EtherCat;
using Xunit;

namespace LearnSmartManufacturing.Tests.Models
{
    public class EtherCatProtocolTests
    {
        [Fact]
        public void BuildAndParseLrwEthernetFrame_RoundTripsAllWireFields()
        {
            byte[] payload = [0x27, 0x00, 0x10, 0x27, 0x00, 0x00, 0x64, 0x00, 0x05, 0x00];
            var frame = EtherCatFrameCodec.BuildLrwEthernetFrame(datagramIndex: 0x2A, logicalAddress: 0x0100_0000, payload, wkc: 9);

            Assert.Equal(14 + 2 + 10 + payload.Length + 2, frame.Length);
            Assert.Equal(0x88, frame[12]);
            Assert.Equal(0xA4, frame[13]);

            var ok = EtherCatFrameCodec.TryParseLrwEthernetFrame(frame, out var cmd, out var idx, out var addr, out var len, out var wkc);
            Assert.True(ok);
            Assert.Equal(EtherCatCommand.Lrw, cmd);
            Assert.Equal((byte)0x2A, idx);
            Assert.Equal(0x0100_0000u, addr);
            Assert.Equal(payload.Length, len);
            Assert.Equal((ushort)9, wkc);
        }

        [Fact]
        public void DissectEthernetFrame_MarksWkcOkOrWkcErr()
        {
            byte[] payload = new byte[18];
            var frameOk = EtherCatFrameCodec.BuildLrwEthernetFrame(1, 0x0100_0000, payload, wkc: 9);
            var segsOk = EtherCatFrameCodec.DissectEthernetFrame(frameOk, expectedWkc: 9);
            Assert.Equal(5, segsOk.Count);
            Assert.Equal("WkcOk", segsOk[^1].Kind);

            var frameErr = EtherCatFrameCodec.BuildLrwEthernetFrame(1, 0x0100_0000, payload, wkc: 3);
            var segsErr = EtherCatFrameCodec.DissectEthernetFrame(frameErr, expectedWkc: 9);
            Assert.Equal("WkcErr", segsErr[^1].Kind);
        }

        [Theory]
        [InlineData(EsmState.Init, EsmState.PreOp, true, 0x0000)]
        [InlineData(EsmState.PreOp, EsmState.SafeOp, true, 0x0000)]
        [InlineData(EsmState.SafeOp, EsmState.Op, true, 0x0000)]
        [InlineData(EsmState.Op, EsmState.SafeOp, true, 0x0000)]
        [InlineData(EsmState.Op, EsmState.Init, true, 0x0000)]
        [InlineData(EsmState.Init, EsmState.SafeOp, false, 0x0011)]
        [InlineData(EsmState.Init, EsmState.Op, false, 0x0011)]
        [InlineData(EsmState.PreOp, EsmState.Op, false, 0x0011)]
        public void EsmTransitionValidation_EnforcesEtg1000Rules(EsmState from, EsmState to, bool expectedValid, ushort expectedAl)
        {
            var ok = EtherCatFrameCodec.IsValidEsmTransition(from, to, out var alCode, out _);
            Assert.Equal(expectedValid, ok);
            Assert.Equal(expectedAl, alCode);
        }

        [Theory]
        [InlineData(Cia402State.SwitchOnDisabled)]
        [InlineData(Cia402State.ReadyToSwitchOn)]
        [InlineData(Cia402State.SwitchedOn)]
        [InlineData(Cia402State.OperationEnabled)]
        [InlineData(Cia402State.Fault)]
        public void Cia402Statusword_RoundTripsAllStates(Cia402State state)
        {
            var sw = EtherCatFrameCodec.BuildCia402Statusword(state);
            var decoded = EtherCatFrameCodec.DecodeCia402Statusword(sw);
            Assert.Equal(state, decoded);
        }
    }
}
