using System.Buffers.Binary;
using LearnSmartManufacturing.Models.Modbus;

namespace LearnSmartManufacturing.Services.Modbus
{
    /// <summary>
    /// In-memory simulated industrial PLC Modbus slave supporting Coils (0x), Discrete Inputs (1x),
    /// Input Registers (3x), Holding Registers (4x), live sensor dynamics, and fault injection.
    /// </summary>
    public sealed class SimulatedModbusSlave
    {
        public const int RegisterCount = 16;
        public const int BitCount = 16;

        private readonly Lock _sync = new();
        private readonly bool[] _coils = new bool[BitCount];
        private readonly bool[] _discreteInputs = new bool[BitCount];
        private readonly ushort[] _holdingRegisters = new ushort[RegisterCount];
        private readonly ushort[] _inputRegisters = new ushort[RegisterCount];

        private double _actualTempC = 224.8;
        private int _productionCount = 12840;
        private int _tickCounter;
        private ModbusFaultInjection _nextFault = ModbusFaultInjection.None;

        public SimulatedModbusSlave(byte unitId = 1, ModbusByteOrder defaultOrder = ModbusByteOrder.CDAB)
        {
            UnitId = unitId;
            DefaultByteOrder = defaultOrder;

            // Default industrial PLC state
            _coils[0] = true;  // 00001 RunEnable
            _coils[1] = true;  // 00002 HeaterEnable
            _coils[2] = false; // 00003 FanEnable
            _coils[3] = false; // 00004 AlarmLamp

            _discreteInputs[0] = true; // 10001 SafetyDoorClosed
            _discreteInputs[1] = true; // 10002 AirPressureOk
            _discreteInputs[2] = true; // 10003 EStopReleased
            _discreteInputs[3] = true; // 10004 PartSensor

            WriteFloatLocked(_holdingRegisters, 0, 225.5f, defaultOrder);
            _holdingRegisters[2] = 450; // 45.0 mm/s (0.1 mm/s scale)
            WriteInt32Locked(_holdingRegisters, 3, _productionCount, defaultOrder);
            _holdingRegisters[5] = 2026; // Recipe ID
            _holdingRegisters[6] = 125;  // PID Kp = 1.25
            _holdingRegisters[7] = 260;  // Alarm upper limit (°C)

            WriteFloatLocked(_inputRegisters, 0, (float)_actualTempC, defaultOrder);
            _inputRegisters[2] = 182; // 18.2 A
            _inputRegisters[3] = 62;  // 0.62 MPa
        }

        public byte UnitId { get; }
        public ModbusByteOrder DefaultByteOrder { get; }

        public void ArmOneShotFault(ModbusFaultInjection fault)
        {
            lock (_sync)
            {
                _nextFault = fault;
            }
        }

        /// <summary>Advances simulated PLC process variables (temperature, current, production counter).</summary>
        public void AdvanceProcessSimulation()
        {
            lock (_sync)
            {
                _tickCounter++;
                var running = _coils[0];
                var heating = _coils[1];
                var cooling = _coils[2];

                var targetTemp = ModbusCodec.DecodeFloat32(_holdingRegisters[0], _holdingRegisters[1], DefaultByteOrder);
                if (!float.IsFinite(targetTemp) || targetTemp < 20 || targetTemp > 400)
                {
                    targetTemp = 225.5f;
                }

                var desired = heating ? targetTemp : (cooling ? 35.0 : 55.0);
                var stepRate = heating ? 0.18 : (cooling ? 0.14 : 0.06);
                var ripple = Math.Sin(_tickCounter * 0.7) * 0.35;
                _actualTempC += (desired - _actualTempC) * stepRate + ripple * 0.12;
                _actualTempC = Math.Clamp(_actualTempC, 20.0, 380.0);

                WriteFloatLocked(_inputRegisters, 0, (float)_actualTempC, DefaultByteOrder);
                _inputRegisters[2] = (ushort)(heating ? 175 + (int)(Math.Abs(Math.Sin(_tickCounter * 0.5)) * 18) : 12);
                _inputRegisters[3] = (ushort)(61 + (_tickCounter % 3));

                if (running)
                {
                    _productionCount++;
                    WriteInt32Locked(_holdingRegisters, 3, _productionCount, DefaultByteOrder);
                    _discreteInputs[3] = (_tickCounter % 2) == 0;
                }
            }
        }

        public byte[] ProcessAdu(ModbusFrameFormat format, ReadOnlySpan<byte> requestAdu)
        {
            if (!ModbusCodec.TryUnwrapAdu(format, requestAdu, out var transId, out var unitId, out var pdu, out _))
            {
                return [];
            }

            if (pdu.Length == 0)
            {
                return [];
            }

            lock (_sync)
            {
                var fcByte = pdu[0];
                var fault = _nextFault;
                _nextFault = ModbusFaultInjection.None;

                if (fault == ModbusFaultInjection.IllegalAddress)
                {
                    return ModbusCodec.WrapAdu(format, transId, unitId, [(byte)(fcByte | 0x80), (byte)ModbusExceptionCode.IllegalDataAddress]);
                }

                if (fault == ModbusFaultInjection.SlaveFailure)
                {
                    return ModbusCodec.WrapAdu(format, transId, unitId, [(byte)(fcByte | 0x80), (byte)ModbusExceptionCode.SlaveDeviceFailure]);
                }

                var responsePdu = ExecutePduLocked(pdu);
                var responseAdu = ModbusCodec.WrapAdu(format, transId, unitId, responsePdu);

                if (fault == ModbusFaultInjection.CorruptRtuCrc && format == ModbusFrameFormat.Rtu && responseAdu.Length >= 4)
                {
                    // Flip the last CRC byte to simulate RS-485 bus noise.
                    responseAdu[^1] ^= 0xFF;
                }

                return responseAdu;
            }
        }

        private byte[] ExecutePduLocked(ReadOnlySpan<byte> pdu)
        {
            var fc = (ModbusFunctionCode)pdu[0];
            return fc switch
            {
                ModbusFunctionCode.ReadCoils => HandleReadBits(pdu, _coils),
                ModbusFunctionCode.ReadDiscreteInputs => HandleReadBits(pdu, _discreteInputs),
                ModbusFunctionCode.ReadHoldingRegisters => HandleReadRegisters(pdu, _holdingRegisters),
                ModbusFunctionCode.ReadInputRegisters => HandleReadRegisters(pdu, _inputRegisters),
                ModbusFunctionCode.WriteSingleCoil => HandleWriteSingleCoil(pdu),
                ModbusFunctionCode.WriteSingleRegister => HandleWriteSingleRegister(pdu),
                ModbusFunctionCode.WriteMultipleRegisters => HandleWriteMultipleRegisters(pdu),
                _ => [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalFunction],
            };
        }

        private static byte[] HandleReadBits(ReadOnlySpan<byte> pdu, bool[] bank)
        {
            if (pdu.Length < 5)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataValue];
            }

            var start = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
            var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
            if (count < 1 || count > bank.Length)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataValue];
            }

            if (start + count > bank.Length)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataAddress];
            }

            var byteCount = (count + 7) / 8;
            var resp = new byte[2 + byteCount];
            resp[0] = pdu[0];
            resp[1] = (byte)byteCount;
            for (var i = 0; i < count; i++)
            {
                if (bank[start + i])
                {
                    resp[2 + i / 8] |= (byte)(1 << (i % 8));
                }
            }

            return resp;
        }

        private static byte[] HandleReadRegisters(ReadOnlySpan<byte> pdu, ushort[] bank)
        {
            if (pdu.Length < 5)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataValue];
            }

            var start = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
            var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
            if (count < 1 || count > bank.Length)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataValue];
            }

            if (start + count > bank.Length)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataAddress];
            }

            var byteCount = count * 2;
            var resp = new byte[2 + byteCount];
            resp[0] = pdu[0];
            resp[1] = (byte)byteCount;
            for (var i = 0; i < count; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2 + i * 2, 2), bank[start + i]);
            }

            return resp;
        }

        private byte[] HandleWriteSingleCoil(ReadOnlySpan<byte> pdu)
        {
            if (pdu.Length < 5)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataValue];
            }

            var addr = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
            var rawVal = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
            if (addr >= _coils.Length)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataAddress];
            }

            if (rawVal is not (0x0000 or 0xFF00))
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataValue];
            }

            _coils[addr] = rawVal == 0xFF00;
            return pdu[..5].ToArray();
        }

        private byte[] HandleWriteSingleRegister(ReadOnlySpan<byte> pdu)
        {
            if (pdu.Length < 5)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataValue];
            }

            var addr = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
            var val = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
            if (addr >= _holdingRegisters.Length)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataAddress];
            }

            _holdingRegisters[addr] = val;
            return pdu[..5].ToArray();
        }

        private byte[] HandleWriteMultipleRegisters(ReadOnlySpan<byte> pdu)
        {
            if (pdu.Length < 6)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataValue];
            }

            var start = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
            var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
            var byteCount = pdu[5];
            if (count < 1 || byteCount != count * 2 || pdu.Length < 6 + byteCount)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataValue];
            }

            if (start + count > _holdingRegisters.Length)
            {
                return [(byte)(pdu[0] | 0x80), (byte)ModbusExceptionCode.IllegalDataAddress];
            }

            for (var i = 0; i < count; i++)
            {
                _holdingRegisters[start + i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(6 + i * 2, 2));
            }

            return pdu[..5].ToArray();
        }

        private static void WriteFloatLocked(ushort[] bank, int start, float value, ModbusByteOrder order)
        {
            var (r0, r1) = ModbusCodec.EncodeFloat32(value, order);
            bank[start] = r0;
            bank[start + 1] = r1;
        }

        private static void WriteInt32Locked(ushort[] bank, int start, int value, ModbusByteOrder order)
        {
            var (r0, r1) = ModbusCodec.EncodeInt32(value, order);
            bank[start] = r0;
            bank[start + 1] = r1;
        }
    }
}
