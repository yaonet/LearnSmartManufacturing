using LearnSmartManufacturing.Models.Modbus;

namespace LearnSmartManufacturing.Tests.Models
{
    public class ModbusCodecTests
    {
        [Fact]
        public void ComputeCrc16_MatchesStandardModbusTestVector()
        {
            // Standard Modbus RTU FC03 request: Slave=1, FC=03, Addr=0x0000, Qty=0x000A => CRC=0xCDC5 (wire: C5 CD)
            byte[] payload = [0x01, 0x03, 0x00, 0x00, 0x00, 0x0A];
            var crc = ModbusCodec.ComputeCrc16(payload);
            Assert.Equal((ushort)0xCDC5, crc);

            var rtu = ModbusCodec.WrapAdu(ModbusFrameFormat.Rtu, 0, 1, payload[1..]);
            Assert.Equal([0x01, 0x03, 0x00, 0x00, 0x00, 0x0A, 0xC5, 0xCD], rtu);
            Assert.True(ModbusCodec.VerifyRtuCrc(rtu));

            // Flip 1 bit -> CRC check must fail
            rtu[^1] ^= 0x01;
            Assert.False(ModbusCodec.VerifyRtuCrc(rtu));
        }

        [Fact]
        public void WrapAndUnwrap_TcpAndRtu_RoundtripCleanly()
        {
            var pdu = ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadHoldingRegisters, 0, 8);

            var tcpAdu = ModbusCodec.WrapAdu(ModbusFrameFormat.Tcp, 0x0042, 1, pdu);
            Assert.True(ModbusCodec.TryUnwrapAdu(ModbusFrameFormat.Tcp, tcpAdu, out var tid, out var uid, out var unwrappedTcp, out var errTcp));
            Assert.Equal((ushort)0x0042, tid);
            Assert.Equal((byte)1, uid);
            Assert.Equal(pdu, unwrappedTcp);
            Assert.Equal(string.Empty, errTcp);

            var rtuAdu = ModbusCodec.WrapAdu(ModbusFrameFormat.Rtu, 0, 3, pdu);
            Assert.True(ModbusCodec.TryUnwrapAdu(ModbusFrameFormat.Rtu, rtuAdu, out _, out var rtuUid, out var unwrappedRtu, out var errRtu));
            Assert.Equal((byte)3, rtuUid);
            Assert.Equal(pdu, unwrappedRtu);
            Assert.Equal(string.Empty, errRtu);
        }

        [Theory]
        [InlineData(ModbusByteOrder.ABCD, 0x4361, 0x8000)]
        [InlineData(ModbusByteOrder.CDAB, 0x8000, 0x4361)]
        [InlineData(ModbusByteOrder.DCBA, 0x0080, 0x6143)]
        [InlineData(ModbusByteOrder.BADC, 0x6143, 0x0080)]
        public void Float32ByteOrder_MatchesExpectedRegisterLayoutAndRoundtrips(ModbusByteOrder order, ushort expectedR0, ushort expectedR1)
        {
            const float value = 225.5f; // IEEE-754 big-endian: 43 61 80 00
            var (r0, r1) = ModbusCodec.EncodeFloat32(value, order);

            Assert.Equal(expectedR0, r0);
            Assert.Equal(expectedR1, r1);
            Assert.Equal(value, ModbusCodec.DecodeFloat32(r0, r1, order), precision: 5);
        }

        [Theory]
        [InlineData(ModbusByteOrder.ABCD)]
        [InlineData(ModbusByteOrder.CDAB)]
        [InlineData(ModbusByteOrder.DCBA)]
        [InlineData(ModbusByteOrder.BADC)]
        public void Int32ByteOrder_RoundtripsPositiveAndNegativeValues(ModbusByteOrder order)
        {
            foreach (var sample in new[] { 0, 12840, 123456789, -42000 })
            {
                var (r0, r1) = ModbusCodec.EncodeInt32(sample, order);
                Assert.Equal(sample, ModbusCodec.DecodeInt32(r0, r1, order));
            }
        }

        [Fact]
        public void DissectFrame_IdentifiesExceptionAndCrcErrors()
        {
            var exTcp = ModbusCodec.WrapAdu(ModbusFrameFormat.Tcp, 1, 1, [0x83, 0x02]);
            var tcpBlocks = ModbusCodec.DissectFrame(ModbusFrameFormat.Tcp, exTcp);
            Assert.Contains(tcpBlocks, b => b.Label == "FC|80" && b.Kind == "Error");
            Assert.Contains(tcpBlocks, b => b.Label == "ExCode" && b.HexText == "02");

            var badRtu = ModbusCodec.WrapAdu(ModbusFrameFormat.Rtu, 0, 1, [0x03, 0x00, 0x00, 0x00, 0x02]);
            badRtu[^1] ^= 0xFF;
            var rtuBlocks = ModbusCodec.DissectFrame(ModbusFrameFormat.Rtu, badRtu);
            Assert.Contains(rtuBlocks, b => b.Label == "CRC-16 ✗" && b.Kind == "Error");
        }
    }
}
