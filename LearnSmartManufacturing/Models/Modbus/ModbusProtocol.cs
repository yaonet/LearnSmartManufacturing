using System.Buffers.Binary;

namespace LearnSmartManufacturing.Models.Modbus
{
    public enum ModbusTransportMode
    {
        SimulatedTcp,
        SimulatedRtu,
        RealTcp,
    }

    public enum ModbusFrameFormat
    {
        Tcp,
        Rtu,
    }

    public enum ModbusFunctionCode : byte
    {
        ReadCoils = 0x01,
        ReadDiscreteInputs = 0x02,
        ReadHoldingRegisters = 0x03,
        ReadInputRegisters = 0x04,
        WriteSingleCoil = 0x05,
        WriteSingleRegister = 0x06,
        WriteMultipleRegisters = 0x10,
    }

    public enum ModbusExceptionCode : byte
    {
        None = 0x00,
        IllegalFunction = 0x01,
        IllegalDataAddress = 0x02,
        IllegalDataValue = 0x03,
        SlaveDeviceFailure = 0x04,
    }

    /// <summary>32-bit multi-register byte order conventions used across PLC vendors.</summary>
    public enum ModbusByteOrder
    {
        /// <summary>Big-Endian (Siemens / standard network order).</summary>
        ABCD,

        /// <summary>Big-Endian Word Swap (Inovance / Delta / many Chinese instruments).</summary>
        CDAB,

        /// <summary>Little-Endian (full reverse).</summary>
        DCBA,

        /// <summary>Big-Endian Byte Swap within each 16-bit word.</summary>
        BADC,
    }

    public enum ModbusFaultInjection
    {
        None,
        IllegalAddress,
        SlaveFailure,
        CorruptRtuCrc,
        Timeout,
    }

    public sealed record ModbusFrameBlock(string Label, string HexText, string Kind);

    public sealed record ModbusTransaction(
        DateTime Timestamp,
        ModbusFrameFormat Format,
        ushort TransactionId,
        byte UnitId,
        ModbusFunctionCode FunctionCode,
        byte[] RequestBytes,
        byte[] ResponseBytes,
        bool IsSuccess,
        ModbusExceptionCode ExceptionCode,
        double RttMs,
        string Summary)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
        public string RequestHex => ModbusCodec.ToHexString(RequestBytes);
        public string ResponseHex => ResponseBytes.Length > 0 ? ModbusCodec.ToHexString(ResponseBytes) : "—";
        public string StatusTag => IsSuccess ? "OK" : (ExceptionCode != ModbusExceptionCode.None ? $"EX 0x{(byte)ExceptionCode:X2}" : "ERR");
    }

    public sealed record RegisterRow(
        ushort Address,
        string ModbusAddress,
        string Description,
        ushort RawValue,
        string EngineeringValue)
    {
        public string HexText => $"0x{RawValue:X4}";
        public short SignedValue => unchecked((short)RawValue);
        public string BinaryText => Convert.ToString(RawValue, 2).PadLeft(16, '0');
    }

    /// <summary>
    /// Spec-compliant Modbus TCP (MBAP) and Modbus RTU (CRC-16/Modbus) frame codec and 32-bit byte-order converter.
    /// </summary>
    public static class ModbusCodec
    {
        /// <summary>Computes CRC-16/Modbus (polynomial 0xA001, initial value 0xFFFF).</summary>
        public static ushort ComputeCrc16(ReadOnlySpan<byte> data)
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

        public static bool VerifyRtuCrc(ReadOnlySpan<byte> rtuFrame)
        {
            if (rtuFrame.Length < 4)
            {
                return false;
            }

            var expected = ComputeCrc16(rtuFrame[..^2]);
            var actual = BinaryPrimitives.ReadUInt16LittleEndian(rtuFrame[^2..]);
            return expected == actual;
        }

        public static byte[] BuildReadPdu(ModbusFunctionCode fc, ushort startAddress, ushort quantity)
        {
            if (fc is not (ModbusFunctionCode.ReadCoils or ModbusFunctionCode.ReadDiscreteInputs
                or ModbusFunctionCode.ReadHoldingRegisters or ModbusFunctionCode.ReadInputRegisters))
            {
                throw new ArgumentOutOfRangeException(nameof(fc), "Not a read function code.");
            }

            var pdu = new byte[5];
            pdu[0] = (byte)fc;
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1, 2), startAddress);
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3, 2), quantity);
            return pdu;
        }

        public static byte[] BuildWriteSingleCoilPdu(ushort address, bool on)
        {
            var pdu = new byte[5];
            pdu[0] = (byte)ModbusFunctionCode.WriteSingleCoil;
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1, 2), address);
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3, 2), on ? (ushort)0xFF00 : (ushort)0x0000);
            return pdu;
        }

        public static byte[] BuildWriteSingleRegisterPdu(ushort address, ushort value)
        {
            var pdu = new byte[5];
            pdu[0] = (byte)ModbusFunctionCode.WriteSingleRegister;
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1, 2), address);
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3, 2), value);
            return pdu;
        }

        public static byte[] BuildWriteMultipleRegistersPdu(ushort startAddress, ReadOnlySpan<ushort> values)
        {
            if (values.Length is < 1 or > 123)
            {
                throw new ArgumentOutOfRangeException(nameof(values), "Quantity must be between 1 and 123.");
            }

            var byteCount = (byte)(values.Length * 2);
            var pdu = new byte[6 + byteCount];
            pdu[0] = (byte)ModbusFunctionCode.WriteMultipleRegisters;
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1, 2), startAddress);
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3, 2), (ushort)values.Length);
            pdu[5] = byteCount;
            for (var i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(6 + i * 2, 2), values[i]);
            }

            return pdu;
        }

        /// <summary>Wraps a PDU in a Modbus TCP (MBAP) or Modbus RTU (SlaveID + CRC-16) ADU.</summary>
        public static byte[] WrapAdu(ModbusFrameFormat format, ushort transactionId, byte unitId, ReadOnlySpan<byte> pdu)
        {
            if (format == ModbusFrameFormat.Tcp)
            {
                var adu = new byte[7 + pdu.Length];
                BinaryPrimitives.WriteUInt16BigEndian(adu.AsSpan(0, 2), transactionId);
                BinaryPrimitives.WriteUInt16BigEndian(adu.AsSpan(2, 2), 0x0000); // Protocol ID = 0
                BinaryPrimitives.WriteUInt16BigEndian(adu.AsSpan(4, 2), (ushort)(pdu.Length + 1)); // UnitID + PDU
                adu[6] = unitId;
                pdu.CopyTo(adu.AsSpan(7));
                return adu;
            }

            var rtu = new byte[1 + pdu.Length + 2];
            rtu[0] = unitId;
            pdu.CopyTo(rtu.AsSpan(1));
            var crc = ComputeCrc16(rtu.AsSpan(0, 1 + pdu.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(rtu.AsSpan(1 + pdu.Length, 2), crc);
            return rtu;
        }

        public static bool TryUnwrapAdu(
            ModbusFrameFormat format,
            ReadOnlySpan<byte> adu,
            out ushort transactionId,
            out byte unitId,
            out byte[] pdu,
            out string error)
        {
            transactionId = 0;
            unitId = 0;
            pdu = [];
            error = string.Empty;

            if (format == ModbusFrameFormat.Tcp)
            {
                if (adu.Length < 8)
                {
                    error = "Modbus TCP 帧长度不足（< 8 字节）。";
                    return false;
                }

                transactionId = BinaryPrimitives.ReadUInt16BigEndian(adu[0..2]);
                var protocolId = BinaryPrimitives.ReadUInt16BigEndian(adu[2..4]);
                var length = BinaryPrimitives.ReadUInt16BigEndian(adu[4..6]);
                if (protocolId != 0)
                {
                    error = $"非法 MBAP Protocol ID: 0x{protocolId:X4}。";
                    return false;
                }

                if (adu.Length < 6 + length || length < 2)
                {
                    error = $"MBAP Length 字段 ({length}) 与实际帧长 ({adu.Length}) 不匹配。";
                    return false;
                }

                unitId = adu[6];
                pdu = adu.Slice(7, length - 1).ToArray();
                return true;
            }

            if (adu.Length < 4)
            {
                error = "Modbus RTU 帧长度不足（< 4 字节）。";
                return false;
            }

            if (!VerifyRtuCrc(adu))
            {
                var expected = ComputeCrc16(adu[..^2]);
                var actual = BinaryPrimitives.ReadUInt16LittleEndian(adu[^2..]);
                error = $"RTU CRC-16 校验失败（期望 0x{expected:X4}，实收 0x{actual:X4}）。";
                return false;
            }

            unitId = adu[0];
            pdu = adu[1..^2].ToArray();
            return true;
        }

        public static bool IsExceptionPdu(ReadOnlySpan<byte> pdu, out ModbusExceptionCode code)
        {
            if (pdu.Length >= 2 && (pdu[0] & 0x80) != 0)
            {
                code = (ModbusExceptionCode)pdu[1];
                return true;
            }

            code = ModbusExceptionCode.None;
            return false;
        }

        public static ushort[] ParseReadRegistersPdu(ReadOnlySpan<byte> pdu)
        {
            if (pdu.Length < 2)
            {
                throw new InvalidOperationException("Invalid read registers response PDU.");
            }

            var byteCount = pdu[1];
            if (byteCount % 2 != 0 || pdu.Length < 2 + byteCount)
            {
                throw new InvalidOperationException("Malformed register byte count.");
            }

            var count = byteCount / 2;
            var result = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(2 + i * 2, 2));
            }

            return result;
        }

        public static bool[] ParseReadBitsPdu(ReadOnlySpan<byte> pdu, int quantity)
        {
            if (pdu.Length < 2)
            {
                throw new InvalidOperationException("Invalid read bits response PDU.");
            }

            var byteCount = pdu[1];
            if (pdu.Length < 2 + byteCount)
            {
                throw new InvalidOperationException("Malformed bit byte count.");
            }

            var bits = new bool[quantity];
            for (var i = 0; i < quantity; i++)
            {
                var byteIdx = i / 8;
                var bitIdx = i % 8;
                if (byteIdx < byteCount)
                {
                    bits[i] = (pdu[2 + byteIdx] & (1 << bitIdx)) != 0;
                }
            }

            return bits;
        }

        public static (ushort Reg0, ushort Reg1) EncodeFloat32(float value, ModbusByteOrder order)
        {
            Span<byte> abcd = stackalloc byte[4];
            BinaryPrimitives.WriteSingleBigEndian(abcd, value);
            return PermuteToRegisters(abcd, order);
        }

        public static float DecodeFloat32(ushort reg0, ushort reg1, ModbusByteOrder order)
        {
            Span<byte> abcd = stackalloc byte[4];
            PermuteFromRegisters(reg0, reg1, order, abcd);
            return BinaryPrimitives.ReadSingleBigEndian(abcd);
        }

        public static (ushort Reg0, ushort Reg1) EncodeInt32(int value, ModbusByteOrder order)
        {
            Span<byte> abcd = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(abcd, value);
            return PermuteToRegisters(abcd, order);
        }

        public static int DecodeInt32(ushort reg0, ushort reg1, ModbusByteOrder order)
        {
            Span<byte> abcd = stackalloc byte[4];
            PermuteFromRegisters(reg0, reg1, order, abcd);
            return BinaryPrimitives.ReadInt32BigEndian(abcd);
        }

        public static IReadOnlyList<ModbusFrameBlock> DissectFrame(ModbusFrameFormat format, ReadOnlySpan<byte> frame)
        {
            var blocks = new List<ModbusFrameBlock>();
            if (frame.Length == 0)
            {
                return blocks;
            }

            if (format == ModbusFrameFormat.Tcp && frame.Length >= 8)
            {
                blocks.Add(new ModbusFrameBlock("TransID", ToHexString(frame[0..2]), "Header"));
                blocks.Add(new ModbusFrameBlock("Proto", ToHexString(frame[2..4]), "Header"));
                blocks.Add(new ModbusFrameBlock("Len", ToHexString(frame[4..6]), "Header"));
                blocks.Add(new ModbusFrameBlock("Unit", ToHexString(frame[6..7]), "Unit"));
                var fcByte = frame[7];
                var isEx = (fcByte & 0x80) != 0;
                blocks.Add(new ModbusFrameBlock(isEx ? "FC|80" : "FC", $"{fcByte:X2}", isEx ? "Error" : "Func"));
                if (frame.Length > 8)
                {
                    blocks.Add(new ModbusFrameBlock(isEx ? "ExCode" : "PDU Data", ToHexString(frame[8..]), isEx ? "Error" : "Data"));
                }
            }
            else if (format == ModbusFrameFormat.Rtu && frame.Length >= 4)
            {
                blocks.Add(new ModbusFrameBlock("Slave", $"{frame[0]:X2}", "Unit"));
                var fcByte = frame[1];
                var isEx = (fcByte & 0x80) != 0;
                blocks.Add(new ModbusFrameBlock(isEx ? "FC|80" : "FC", $"{fcByte:X2}", isEx ? "Error" : "Func"));
                if (frame.Length > 4)
                {
                    blocks.Add(new ModbusFrameBlock(isEx ? "ExCode" : "PDU Data", ToHexString(frame[2..^2]), isEx ? "Error" : "Data"));
                }

                var crcOk = VerifyRtuCrc(frame);
                blocks.Add(new ModbusFrameBlock(crcOk ? "CRC-16 ✓" : "CRC-16 ✗", ToHexString(frame[^2..]), crcOk ? "CrcOk" : "Error"));
            }
            else
            {
                blocks.Add(new ModbusFrameBlock("Raw", ToHexString(frame), "Data"));
            }

            return blocks;
        }

        public static string DescribeException(ModbusExceptionCode code) => code switch
        {
            ModbusExceptionCode.IllegalFunction => "0x01 非法功能码 (Illegal Function)",
            ModbusExceptionCode.IllegalDataAddress => "0x02 非法数据地址 (Illegal Data Address)",
            ModbusExceptionCode.IllegalDataValue => "0x03 非法数据值 (Illegal Data Value)",
            ModbusExceptionCode.SlaveDeviceFailure => "0x04 从站设备故障 (Slave Device Failure)",
            _ => $"0x{(byte)code:X2} 未知异常码",
        };

        public static string ToHexString(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            return string.Join(' ', bytes.ToArray().Select(b => b.ToString("X2")));
        }

        private static (ushort Reg0, ushort Reg1) PermuteToRegisters(ReadOnlySpan<byte> abcd, ModbusByteOrder order)
        {
            byte a = abcd[0], b = abcd[1], c = abcd[2], d = abcd[3];
            return order switch
            {
                ModbusByteOrder.ABCD => ((ushort)((a << 8) | b), (ushort)((c << 8) | d)),
                ModbusByteOrder.CDAB => ((ushort)((c << 8) | d), (ushort)((a << 8) | b)),
                ModbusByteOrder.DCBA => ((ushort)((d << 8) | c), (ushort)((b << 8) | a)),
                ModbusByteOrder.BADC => ((ushort)((b << 8) | a), (ushort)((d << 8) | c)),
                _ => throw new ArgumentOutOfRangeException(nameof(order)),
            };
        }

        private static void PermuteFromRegisters(ushort reg0, ushort reg1, ModbusByteOrder order, Span<byte> abcd)
        {
            var b0 = (byte)(reg0 >> 8);
            var b1 = (byte)(reg0 & 0xFF);
            var b2 = (byte)(reg1 >> 8);
            var b3 = (byte)(reg1 & 0xFF);

            switch (order)
            {
                case ModbusByteOrder.ABCD:
                    abcd[0] = b0; abcd[1] = b1; abcd[2] = b2; abcd[3] = b3;
                    break;
                case ModbusByteOrder.CDAB:
                    abcd[0] = b2; abcd[1] = b3; abcd[2] = b0; abcd[3] = b1;
                    break;
                case ModbusByteOrder.DCBA:
                    abcd[0] = b3; abcd[1] = b2; abcd[2] = b1; abcd[3] = b0;
                    break;
                case ModbusByteOrder.BADC:
                    abcd[0] = b1; abcd[1] = b0; abcd[2] = b3; abcd[3] = b2;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(order));
            }
        }
    }
}
