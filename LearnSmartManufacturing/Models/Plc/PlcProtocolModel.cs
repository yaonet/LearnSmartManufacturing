using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace LearnSmartManufacturing.Models.Plc
{
    public enum PlcBrandKind
    {
        SiemensS7,
        MitsubishiMc3E,
        OmronFinsTcp,
    }

    public enum PlcDataType
    {
        Bool,
        Int16,
        Int32,
        Float32,
        S7String,
    }

    public sealed record PlcFrameSegment(string Label, string HexText, string Kind);

    public sealed record PlcTransactionRecord(
        long Id,
        DateTime Timestamp,
        PlcBrandKind Brand,
        string Operation,
        string Address,
        bool IsSuccess,
        string StatusCodeText,
        string DecodedValueText,
        byte[] RequestBytes,
        byte[] ResponseBytes,
        IReadOnlyList<PlcFrameSegment> RequestSegments,
        IReadOnlyList<PlcFrameSegment> ResponseSegments)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
        public string RequestHex => PlcFrameCodec.ToHex(RequestBytes, 24);
        public string ResponseHex => PlcFrameCodec.ToHex(ResponseBytes, 24);
        public string Summary => $"{Operation} {Address} → {DecodedValueText}";
    }

    public sealed record PlcMemoryTagSnapshot(
        string Address,
        string Name,
        PlcDataType DataType,
        int ByteOffset,
        string RawHex,
        string ValueText,
        string Note);

    public sealed record PlcBatchPlanResult(
        int TagCount,
        int MaxGapBytes,
        int PduLimitBytes,
        double RttMs,
        int NaiveRoundTrips,
        double NaiveTotalMs,
        int BatchedRoundTrips,
        double BatchedTotalMs,
        double SpeedupFactor,
        IReadOnlyList<string> BatchSummaries,
        string Verdict);

    public sealed record PlcBrandComparisonRow(
        PlcBrandKind Brand,
        string Name,
        string DefaultPort,
        string EndianRule,
        string HandshakeRule,
        string AddressExamples,
        string TopInterviewPitfall);

    /// <summary>
    /// Wire-format frame encoders and color-coded dissectors for Siemens S7 (TPKT+COTP+S7Comm),
    /// Mitsubishi MC SLMP 3E Binary, and Omron FINS/TCP.
    /// </summary>
    public static class PlcFrameCodec
    {
        public static IReadOnlyList<PlcBrandComparisonRow> BrandComparisons { get; } =
        [
            new(PlcBrandKind.SiemensS7, "西门子 S7 (1200/1500/300)", "TCP 102",
                "大端序 Big-Endian (ABCD)",
                "COTP CR/CC (Rack/Slot TSAP) + S7 SetupComm 协商 PDU",
                "DB1.DBX0.0 · DB1.DBW2 · DB1.DBD4 · M10.0",
                "必须关闭 DB“优化的块访问”并勾选允许 PUT/GET；S7 字符串带 2B [MaxLen][ActLen] 头"),
            new(PlcBrandKind.MitsubishiMc3E, "三菱 MC (SLMP 3E 二进制)", "TCP 5000",
                "小端序 Little-Endian (DCBA)",
                "无额外应用握手，直发 50 00 3E 帧（响应头 D0 00）",
                "D100 (十进制) · M10 · X1A / Y20 (十六进制)",
                "D/M 为十进制寻址而 X/Y/B/W 为十六进制；多字寄存器应单次 0x0401 批量读而非循环单读"),
            new(PlcBrandKind.OmronFinsTcp, "欧姆龙 FINS/TCP (NJ/NX/CJ)", "TCP 9600",
                "Word 内大端 · 32位常为字交换 CDAB",
                "必须先发 20B FINS/TCP 握手帧 (Cmd=0) 获取 SA1/DA1 节点号",
                "D100 (0x82 DM) · W0.00 (0xB1 WR) · CIO10 (0xB0)",
                "未做节点握手直接发 Cmd=2 会被断开；需检查响应末尾 2B EndCode (00 00 才为成功)"),
        ];

        #region Siemens S7 (RFC1006 TPKT + COTP + S7Comm)

        public static byte[] BuildS7ConnectRequest(int rack, int slot)
        {
            // TPKT(4) + COTP CR(18) = 22 bytes; Dst TSAP encodes Rack/Slot: 0x0100 | (rack*32 + slot)
            var dstTsapLow = (byte)((rack * 32) + slot);
            return
            [
                0x03, 0x00, 0x00, 0x16, // TPKT len=22
                0x11, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00, // COTP CR
                0xC1, 0x02, 0x01, 0x00, // Src TSAP
                0xC2, 0x02, 0x01, dstTsapLow, // Dst TSAP (Rack/Slot)
                0xC0, 0x01, 0x0A, // TPDU size 1024
            ];
        }

        public static byte[] BuildS7ConnectResponse(int rack, int slot)
        {
            var dstTsapLow = (byte)((rack * 32) + slot);
            return
            [
                0x03, 0x00, 0x00, 0x16,
                0x11, 0xD0, 0x00, 0x01, 0x00, 0x01, 0x00, // COTP CC (Confirm)
                0xC0, 0x01, 0x0A,
                0xC1, 0x02, 0x01, 0x00,
                0xC2, 0x02, 0x01, dstTsapLow,
            ];
        }

        public static byte[] BuildS7ReadRequest(ushort pduRef, byte areaCode, ushort dbNumber, int byteOffset, int bitOffset, ushort readLength)
        {
            // TPKT(4) + COTP DT(3) + S7 Hdr(10) + Read Param(14) = 31 bytes
            var buf = new byte[31];
            buf[0] = 0x03; buf[1] = 0x00;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2, 2), (ushort)buf.Length);
            buf[4] = 0x02; buf[5] = 0xF0; buf[6] = 0x80; // COTP DT Data

            buf[7] = 0x32; // S7 Protocol ID
            buf[8] = 0x01; // ROSCTR Job Request
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(11, 2), pduRef);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(13, 2), 14); // Param Len
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(15, 2), 0);  // Data Len

            // S7 Read Parameter (14B)
            buf[17] = 0x04; // Function: Read Var
            buf[18] = 0x01; // Item Count = 1
            buf[19] = 0x12; buf[20] = 0x0A; buf[21] = 0x10; // Var Spec (S7-Any)
            buf[22] = 0x02; // Transport Size: BYTE
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(23, 2), readLength);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(25, 2), dbNumber);
            buf[27] = areaCode; // 0x84=DB, 0x83=M
            var bitAddr = (byteOffset * 8) + bitOffset;
            buf[28] = (byte)((bitAddr >> 16) & 0xFF);
            buf[29] = (byte)((bitAddr >> 8) & 0xFF);
            buf[30] = (byte)(bitAddr & 0xFF);
            return buf;
        }

        public static byte[] BuildS7ReadResponse(ushort pduRef, byte returnCode, ReadOnlySpan<byte> payload)
        {
            // TPKT(4) + COTP DT(3) + S7 Ack_Data Hdr(12) + Param(2) + DataItem(4 + payload)
            var dataLen = 4 + payload.Length;
            var buf = new byte[4 + 3 + 12 + 2 + dataLen];
            buf[0] = 0x03; buf[1] = 0x00;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2, 2), (ushort)buf.Length);
            buf[4] = 0x02; buf[5] = 0xF0; buf[6] = 0x80;

            buf[7] = 0x32;
            buf[8] = 0x03; // Ack_Data
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(11, 2), pduRef);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(13, 2), 2);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(15, 2), (ushort)dataLen);
            // ErrorClass/Code at 17, 18 = 0x00 0x00
            buf[19] = 0x04; buf[20] = 0x01; // Read Var, 1 Item
            buf[21] = returnCode; // 0xFF = Success, 0x05 = Address out of range
            buf[22] = 0x04; // Byte/Word transport size
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(23, 2), (ushort)(payload.Length * 8)); // Bit length
            payload.CopyTo(buf.AsSpan(25));
            return buf;
        }

        public static byte[] BuildS7WriteRequest(ushort pduRef, byte areaCode, ushort dbNumber, int byteOffset, int bitOffset, ReadOnlySpan<byte> payload)
        {
            var dataLen = 4 + payload.Length;
            var buf = new byte[4 + 3 + 10 + 14 + dataLen];
            buf[0] = 0x03; buf[1] = 0x00;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2, 2), (ushort)buf.Length);
            buf[4] = 0x02; buf[5] = 0xF0; buf[6] = 0x80;

            buf[7] = 0x32; buf[8] = 0x01;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(11, 2), pduRef);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(13, 2), 14);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(15, 2), (ushort)dataLen);

            buf[17] = 0x05; // Function: Write Var
            buf[18] = 0x01;
            buf[19] = 0x12; buf[20] = 0x0A; buf[21] = 0x10;
            buf[22] = 0x02;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(23, 2), (ushort)payload.Length);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(25, 2), dbNumber);
            buf[27] = areaCode;
            var bitAddr = (byteOffset * 8) + bitOffset;
            buf[28] = (byte)((bitAddr >> 16) & 0xFF);
            buf[29] = (byte)((bitAddr >> 8) & 0xFF);
            buf[30] = (byte)(bitAddr & 0xFF);

            buf[31] = 0x00; buf[32] = 0x04;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(33, 2), (ushort)(payload.Length * 8));
            payload.CopyTo(buf.AsSpan(35));
            return buf;
        }

        public static byte[] BuildS7WriteResponse(ushort pduRef, byte returnCode)
        {
            var buf = new byte[22];
            buf[0] = 0x03; buf[1] = 0x00;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2, 2), (ushort)buf.Length);
            buf[4] = 0x02; buf[5] = 0xF0; buf[6] = 0x80;
            buf[7] = 0x32; buf[8] = 0x03;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(11, 2), pduRef);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(13, 2), 2);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(15, 2), 1);
            buf[19] = 0x05; buf[20] = 0x01;
            buf[21] = returnCode; // 0xFF = Success
            return buf;
        }

        public static IReadOnlyList<PlcFrameSegment> DissectS7Frame(ReadOnlySpan<byte> frame, bool isResponse)
        {
            if (frame.Length < 10) return [];
            var segs = new List<PlcFrameSegment>
            {
                new("TPKT (RFC1006)", ToHex(frame[..4]), "Hdr"),
            };

            if (frame[5] is 0xE0 or 0xD0)
            {
                segs.Add(new(frame[5] == 0xE0 ? "COTP CR (建连)" : "COTP CC (确认)", ToHex(frame[4..11]), "Cmd"));
                segs.Add(new("Rack/Slot TSAP", ToHex(frame[11..]), "Data"));
                return segs;
            }

            segs.Add(new("COTP DT", ToHex(frame.Slice(4, 3)), "Route"));
            var hdrLen = isResponse ? 12 : 10;
            if (frame.Length < 7 + hdrLen) return segs;
            segs.Add(new($"S7 Hdr ({(isResponse ? "Ack" : "Job")})", ToHex(frame.Slice(7, hdrLen), 12), "Cmd"));

            var paramLen = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(13, 2));
            var dataLen = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(15, 2));
            var offset = 7 + hdrLen;
            if (paramLen > 0 && offset + paramLen <= frame.Length)
            {
                var fn = frame[offset] == 0x04 ? "Read 0x04" : frame[offset] == 0x05 ? "Write 0x05" : $"Fn 0x{frame[offset]:X2}";
                segs.Add(new($"S7 Param ({fn})", ToHex(frame.Slice(offset, paramLen), 10), "Area"));
                offset += paramLen;
            }

            if (dataLen > 0 && offset < frame.Length)
            {
                var dataSpan = frame[offset..];
                var ok = !isResponse || dataSpan[0] == 0xFF;
                segs.Add(new(ok ? $"Data ({dataSpan.Length}B)" : $"Err 0x{dataSpan[0]:X2}", ToHex(dataSpan, 10), ok ? "Ok" : "Err"));
            }

            return segs;
        }

        #endregion

        #region Mitsubishi MC Protocol (SLMP 3E Binary)

        public static byte[] BuildMc3EReadRequest(byte deviceCode, int headDevice, ushort wordCount, bool isBit = false)
        {
            // SubHdr(2) + Route(5) + DataLen(2) + Timer(2) + Cmd(2) + SubCmd(2) + HeadDev(3) + DevCode(1) + Points(2) = 21B
            var buf = new byte[21];
            buf[0] = 0x50; buf[1] = 0x00; // 3E Binary Request
            buf[2] = 0x00; buf[3] = 0xFF; buf[4] = 0xFF; buf[5] = 0x03; buf[6] = 0x00; // Network/PC/IO/Station
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(7, 2), 12); // Remaining length
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(9, 2), 0x0010); // Monitor Timer
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(11, 2), 0x0401); // Batch Read
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(13, 2), isBit ? (ushort)0x0001 : (ushort)0x0000);
            buf[15] = (byte)(headDevice & 0xFF);
            buf[16] = (byte)((headDevice >> 8) & 0xFF);
            buf[17] = (byte)((headDevice >> 16) & 0xFF);
            buf[18] = deviceCode;
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(19, 2), wordCount);
            return buf;
        }

        public static byte[] BuildMc3EWriteRequest(byte deviceCode, int headDevice, ReadOnlySpan<byte> payload, ushort points, bool isBit = false)
        {
            var buf = new byte[21 + payload.Length];
            buf[0] = 0x50; buf[1] = 0x00;
            buf[2] = 0x00; buf[3] = 0xFF; buf[4] = 0xFF; buf[5] = 0x03; buf[6] = 0x00;
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(7, 2), (ushort)(12 + payload.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(9, 2), 0x0010);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(11, 2), 0x1401); // Batch Write
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(13, 2), isBit ? (ushort)0x0001 : (ushort)0x0000);
            buf[15] = (byte)(headDevice & 0xFF);
            buf[16] = (byte)((headDevice >> 8) & 0xFF);
            buf[17] = (byte)((headDevice >> 16) & 0xFF);
            buf[18] = deviceCode;
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(19, 2), points);
            payload.CopyTo(buf.AsSpan(21));
            return buf;
        }

        public static byte[] BuildMc3EResponse(ushort endCode, ReadOnlySpan<byte> payload)
        {
            var buf = new byte[11 + payload.Length];
            buf[0] = 0xD0; buf[1] = 0x00; // 3E Binary Response
            buf[2] = 0x00; buf[3] = 0xFF; buf[4] = 0xFF; buf[5] = 0x03; buf[6] = 0x00;
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(7, 2), (ushort)(2 + payload.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(9, 2), endCode);
            payload.CopyTo(buf.AsSpan(11));
            return buf;
        }

        public static IReadOnlyList<PlcFrameSegment> DissectMc3EFrame(ReadOnlySpan<byte> frame, bool isResponse)
        {
            if (frame.Length < 11) return [];
            var segs = new List<PlcFrameSegment>
            {
                new(isResponse ? "3E Resp (D0 00)" : "3E Req (50 00)", ToHex(frame[..2]), "Hdr"),
                new("Net/PC/IO", ToHex(frame.Slice(2, 5)), "Route"),
                new("Len (LE)", ToHex(frame.Slice(7, 2)), "Area"),
            };

            if (isResponse)
            {
                var endCode = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(9, 2));
                segs.Add(new(endCode == 0 ? "EndCode OK" : $"Err 0x{endCode:X4}", ToHex(frame.Slice(9, 2)), endCode == 0 ? "Ok" : "Err"));
                if (frame.Length > 11)
                {
                    segs.Add(new($"Data ({frame.Length - 11}B LE)", ToHex(frame[11..], 10), "Data"));
                }
            }
            else if (frame.Length >= 21)
            {
                var cmd = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(11, 2));
                segs.Add(new($"Cmd 0x{cmd:X4}", ToHex(frame.Slice(11, 4)), "Cmd"));
                segs.Add(new($"Dev 0x{frame[18]:X2}", ToHex(frame.Slice(15, 6)), "Area"));
                if (frame.Length > 21)
                {
                    segs.Add(new($"WriteData ({frame.Length - 21}B)", ToHex(frame[21..], 10), "Data"));
                }
            }

            return segs;
        }

        #endregion

        #region Omron FINS/TCP

        public static byte[] BuildFinsHandshakePacket(byte clientNode = 0x0A)
        {
            var buf = new byte[20];
            Encoding.ASCII.GetBytes("FINS").CopyTo(buf, 0);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4, 4), 12);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8, 4), 0); // Cmd 0 = Node Addr Request
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(16, 4), clientNode);
            return buf;
        }

        public static byte[] BuildFinsHandshakeResponse(byte clientNode = 0x0A, byte serverNode = 0x01)
        {
            var buf = new byte[24];
            Encoding.ASCII.GetBytes("FINS").CopyTo(buf, 0);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4, 4), 16);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8, 4), 1); // Cmd 1 = Node Addr Response
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(16, 4), clientNode);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(20, 4), serverNode);
            return buf;
        }

        public static byte[] BuildFinsReadRequest(byte areaCode, ushort startAddr, byte bitOffset, ushort wordCount, byte sid)
        {
            // FINS/TCP Header(16) + FINS Frame Header(10) + MRC/SRC(2) + Area(1) + Addr(2) + Bit(1) + Count(2) = 34B
            var buf = new byte[34];
            Encoding.ASCII.GetBytes("FINS").CopyTo(buf, 0);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4, 4), 26); // len after first 8B
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8, 4), 2);  // Cmd 2 = FINS Frame Send
            // FINS Header (10B)
            buf[16] = 0x80; buf[17] = 0x00; buf[18] = 0x02; // ICF, RSV, GCT
            buf[19] = 0x00; buf[20] = 0x01; buf[21] = 0x00; // DNA, DA1(PLC=1), DA2
            buf[22] = 0x00; buf[23] = 0x0A; buf[24] = 0x00; // SNA, SA1(PC=10), SA2
            buf[25] = sid;
            // Command 01 01 = Memory Area Read
            buf[26] = 0x01; buf[27] = 0x01;
            buf[28] = areaCode;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(29, 2), startAddr);
            buf[31] = bitOffset;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(32, 2), wordCount);
            return buf;
        }

        public static byte[] BuildFinsWriteRequest(byte areaCode, ushort startAddr, byte bitOffset, ushort wordCount, ReadOnlySpan<byte> payload, byte sid)
        {
            var buf = new byte[34 + payload.Length];
            Encoding.ASCII.GetBytes("FINS").CopyTo(buf, 0);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4, 4), (uint)(26 + payload.Length));
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8, 4), 2);
            buf[16] = 0x80; buf[17] = 0x00; buf[18] = 0x02;
            buf[19] = 0x00; buf[20] = 0x01; buf[21] = 0x00;
            buf[22] = 0x00; buf[23] = 0x0A; buf[24] = 0x00;
            buf[25] = sid;
            buf[26] = 0x01; buf[27] = 0x02; // Memory Area Write
            buf[28] = areaCode;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(29, 2), startAddr);
            buf[31] = bitOffset;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(32, 2), wordCount);
            payload.CopyTo(buf.AsSpan(34));
            return buf;
        }

        public static byte[] BuildFinsResponse(byte mrc, byte src, ushort endCode, ReadOnlySpan<byte> payload, byte sid)
        {
            var buf = new byte[30 + payload.Length];
            Encoding.ASCII.GetBytes("FINS").CopyTo(buf, 0);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4, 4), (uint)(22 + payload.Length));
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8, 4), 2);
            buf[16] = 0xC0; buf[17] = 0x00; buf[18] = 0x02; // ICF=0xC0 Response
            buf[19] = 0x00; buf[20] = 0x0A; buf[21] = 0x00; // Dest = PC 10
            buf[22] = 0x00; buf[23] = 0x01; buf[24] = 0x00; // Src = PLC 1
            buf[25] = sid;
            buf[26] = mrc; buf[27] = src;
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(28, 2), endCode);
            payload.CopyTo(buf.AsSpan(30));
            return buf;
        }

        public static IReadOnlyList<PlcFrameSegment> DissectFinsFrame(ReadOnlySpan<byte> frame, bool isResponse)
        {
            if (frame.Length < 16) return [];
            var cmd = BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(8, 4));
            var segs = new List<PlcFrameSegment>
            {
                new("FINS/TCP (46 49 4E 53)", ToHex(frame[..12], 8), "Hdr"),
            };

            if (cmd is 0 or 1)
            {
                segs.Add(new(cmd == 0 ? "节点握手请求" : "节点握手确认 (SA1/DA1)", ToHex(frame[12..]), "Ok"));
                return segs;
            }

            if (frame.Length < 28) return segs;
            segs.Add(new("FINS Hdr (DA1/SA1)", ToHex(frame.Slice(16, 10), 8), "Route"));
            segs.Add(new($"MRC/SRC ({frame[26]:X2} {frame[27]:X2})", ToHex(frame.Slice(26, 2)), "Cmd"));

            if (isResponse && frame.Length >= 30)
            {
                var endCode = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(28, 2));
                segs.Add(new(endCode == 0 ? "EndCode 00 00 OK" : $"Err 0x{endCode:X4}", ToHex(frame.Slice(28, 2)), endCode == 0 ? "Ok" : "Err"));
                if (frame.Length > 30)
                {
                    segs.Add(new($"Data ({frame.Length - 30}B)", ToHex(frame[30..], 10), "Data"));
                }
            }
            else if (frame.Length >= 34)
            {
                segs.Add(new($"Area 0x{frame[28]:X2}", ToHex(frame.Slice(28, 6)), "Area"));
                if (frame.Length > 34)
                {
                    segs.Add(new($"WriteData ({frame.Length - 34}B)", ToHex(frame[34..], 10), "Data"));
                }
            }

            return segs;
        }

        #endregion

        /// <summary>Evaluates naive per-tag polling vs PDU/gap-aware batch block merging optimization.</summary>
        public static PlcBatchPlanResult EvaluateBatchOptimization(int tagCount, int maxGapBytes, int pduLimitBytes, double rttMs)
        {
            var count = Math.Clamp(tagCount, 5, 120);
            var gap = Math.Clamp(maxGapBytes, 0, 64);
            var maxPayload = Math.Max(32, pduLimitBytes - 24);
            var rtt = Math.Max(0.5, rttMs);

            // Simulate realistic industrial PLC tag layout (each tag 4 bytes, clustered with periodic gaps of 0..16 bytes)
            var offsets = new List<int>(count);
            var cur = 0;
            for (var i = 0; i < count; i++)
            {
                offsets.Add(cur);
                cur += 4;
                if ((i + 1) % 8 == 0) cur += 12; // Cluster gap (e.g. between machine stations)
                else if ((i + 1) % 3 == 0) cur += 4; // Small alignment pad
            }

            var batches = new List<(int Start, int End, int Count)>();
            var bStart = offsets[0];
            var bEnd = offsets[0] + 4;
            var bCount = 1;

            for (var i = 1; i < offsets.Count; i++)
            {
                var nextStart = offsets[i];
                var nextEnd = nextStart + 4;
                var hole = nextStart - bEnd;
                var mergedSpan = nextEnd - bStart;

                if (hole <= gap && mergedSpan <= maxPayload)
                {
                    bEnd = nextEnd;
                    bCount++;
                }
                else
                {
                    batches.Add((bStart, bEnd, bCount));
                    bStart = nextStart;
                    bEnd = nextEnd;
                    bCount = 1;
                }
            }

            batches.Add((bStart, bEnd, bCount));

            var naiveMs = count * rtt;
            var batchedMs = batches.Count * rtt;
            var speedup = naiveMs / Math.Max(0.1, batchedMs);

            var summaries = batches.Select((b, idx) =>
                $"Batch #{idx + 1}: 偏移 {b.Start}..{b.End - 1} ({b.End - b.Start} B · 合并 {b.Count} 点)").ToList();

            var verdict = string.Create(CultureInfo.InvariantCulture,
                $"批量打包将 {count} 次 TCP 往返压缩为 {batches.Count} 次（耗时 {naiveMs:F1} ms → {batchedMs:F1} ms，提速 {speedup:F1}×）！");

            return new PlcBatchPlanResult(
                count, gap, pduLimitBytes, rtt,
                count, Math.Round(naiveMs, 1),
                batches.Count, Math.Round(batchedMs, 1),
                Math.Round(speedup, 1),
                summaries, verdict);
        }

        public static string ToHex(ReadOnlySpan<byte> bytes, int maxBytes = 20)
        {
            if (bytes.Length == 0) return string.Empty;
            var count = Math.Min(bytes.Length, maxBytes);
            var hex = string.Join(' ', bytes[..count].ToArray().Select(b => b.ToString("X2")));
            return bytes.Length > maxBytes ? $"{hex} … ({bytes.Length}B)" : hex;
        }
    }
}
