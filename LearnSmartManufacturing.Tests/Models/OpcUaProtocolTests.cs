using LearnSmartManufacturing.Models.OpcUa;
using Xunit;

namespace LearnSmartManufacturing.Tests.Models
{
    public class OpcUaProtocolTests
    {
        [Fact]
        public void BuildHelloAndOpenPackets_RoundTripUacpHeaderFields()
        {
            var hel = OpcUaBinaryCodec.BuildHelloPacket("opc.tcp://127.0.0.1:4840/SmartFactory/UA");
            Assert.True(OpcUaBinaryCodec.TryParseUacpHeader(hel, out var helType, out var helChunk, out var helSize));
            Assert.Equal("HEL", helType);
            Assert.Equal('F', helChunk);
            Assert.Equal((uint)hel.Length, helSize);

            var opn = OpcUaBinaryCodec.BuildOpenSecureChannelPacket(1001, "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256");
            Assert.True(OpcUaBinaryCodec.TryParseUacpHeader(opn, out var opnType, out var opnChunk, out var opnSize));
            Assert.Equal("OPN", opnType);
            Assert.Equal('F', opnChunk);
            Assert.Equal((uint)opn.Length, opnSize);
        }

        [Fact]
        public void StatusCodeHelper_ClassifiesSeverityBitsAccurately()
        {
            Assert.True(OpcUaStatusCodes.IsGood(OpcUaStatusCodes.Good));
            Assert.True(OpcUaStatusCodes.IsUncertain(OpcUaStatusCodes.UncertainLastUsableValue));
            Assert.True(OpcUaStatusCodes.IsBad(OpcUaStatusCodes.BadCertificateUntrusted));
            Assert.True(OpcUaStatusCodes.IsBad(OpcUaStatusCodes.BadNotWritable));
            Assert.True(OpcUaStatusCodes.IsBad(OpcUaStatusCodes.BadSensorFailure));
            Assert.Contains("0x801A0000", OpcUaStatusCodes.Format(OpcUaStatusCodes.BadCertificateUntrusted));
        }
    }
}
