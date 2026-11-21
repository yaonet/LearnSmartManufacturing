using LearnSmartManufacturing.Models.Modbus;
using LearnSmartManufacturing.Services.Modbus;

namespace LearnSmartManufacturing.Tests.Services
{
    public class SimulatedModbusSlaveTests
    {
        [Theory]
        [InlineData(ModbusFrameFormat.Tcp)]
        [InlineData(ModbusFrameFormat.Rtu)]
        public void ReadAndWriteHoldingRegisters_WorksOverTcpAndRtu(ModbusFrameFormat format)
        {
            var slave = new SimulatedModbusSlave();

            // Write Float32 248.5 using FC 0x10
            var (r0, r1) = ModbusCodec.EncodeFloat32(248.5f, ModbusByteOrder.CDAB);
            var writePdu = ModbusCodec.BuildWriteMultipleRegistersPdu(0, [r0, r1]);
            var writeResp = slave.ProcessAdu(format, ModbusCodec.WrapAdu(format, 1, 1, writePdu));
            Assert.True(ModbusCodec.TryUnwrapAdu(format, writeResp, out _, out _, out var writeRespPdu, out _));
            Assert.False(ModbusCodec.IsExceptionPdu(writeRespPdu, out _));

            // Read back using FC 0x03
            var readPdu = ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadHoldingRegisters, 0, 2);
            var readResp = slave.ProcessAdu(format, ModbusCodec.WrapAdu(format, 2, 1, readPdu));
            Assert.True(ModbusCodec.TryUnwrapAdu(format, readResp, out _, out _, out var readRespPdu, out _));
            var regs = ModbusCodec.ParseReadRegistersPdu(readRespPdu);
            Assert.Equal(248.5f, ModbusCodec.DecodeFloat32(regs[0], regs[1], ModbusByteOrder.CDAB), precision: 4);
        }

        [Fact]
        public void WriteSingleCoilAndReadBack_UpdatesBitBank()
        {
            var slave = new SimulatedModbusSlave();

            var writeCoil = ModbusCodec.WrapAdu(ModbusFrameFormat.Tcp, 1, 1, ModbusCodec.BuildWriteSingleCoilPdu(2, true));
            slave.ProcessAdu(ModbusFrameFormat.Tcp, writeCoil);

            var readCoils = ModbusCodec.WrapAdu(ModbusFrameFormat.Tcp, 2, 1, ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadCoils, 0, 4));
            var resp = slave.ProcessAdu(ModbusFrameFormat.Tcp, readCoils);
            Assert.True(ModbusCodec.TryUnwrapAdu(ModbusFrameFormat.Tcp, resp, out _, out _, out var pdu, out _));
            var bits = ModbusCodec.ParseReadBitsPdu(pdu, 4);
            Assert.True(bits[2]);
        }

        [Fact]
        public void OutOfBoundsAddress_ReturnsIllegalDataAddressException()
        {
            var slave = new SimulatedModbusSlave();
            var req = ModbusCodec.WrapAdu(ModbusFrameFormat.Tcp, 1, 1,
                ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadHoldingRegisters, 100, 4));

            var resp = slave.ProcessAdu(ModbusFrameFormat.Tcp, req);
            Assert.True(ModbusCodec.TryUnwrapAdu(ModbusFrameFormat.Tcp, resp, out _, out _, out var pdu, out _));
            Assert.True(ModbusCodec.IsExceptionPdu(pdu, out var code));
            Assert.Equal(ModbusExceptionCode.IllegalDataAddress, code);
        }

        [Fact]
        public void ArmedCorruptCrcFault_ProducesFailingRtuFrame()
        {
            var slave = new SimulatedModbusSlave();
            slave.ArmOneShotFault(ModbusFaultInjection.CorruptRtuCrc);

            var req = ModbusCodec.WrapAdu(ModbusFrameFormat.Rtu, 0, 1,
                ModbusCodec.BuildReadPdu(ModbusFunctionCode.ReadHoldingRegisters, 0, 2));
            var resp = slave.ProcessAdu(ModbusFrameFormat.Rtu, req);

            Assert.False(ModbusCodec.VerifyRtuCrc(resp));
            Assert.False(ModbusCodec.TryUnwrapAdu(ModbusFrameFormat.Rtu, resp, out _, out _, out _, out var err));
            Assert.Contains("CRC-16", err);
        }
    }
}
