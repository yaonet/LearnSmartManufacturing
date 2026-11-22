using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LearnSmartManufacturing.Models.Plc;

namespace LearnSmartManufacturing.Services.Plc
{
    /// <summary>
    /// Built-in multi-brand PLC memory simulator supporting Siemens S7 (Big-Endian DB + S7 String),
    /// Mitsubishi MC 3E (Little-Endian D/M/X/Y), and Omron FINS/TCP (DM/WR/CIO) with authentic wire frames.
    /// </summary>
    public sealed class SimulatedPlcEngine
    {
        private readonly byte[] _s7Db1 = new byte[64];
        private readonly byte[] _mcD = new byte[256]; // D100..D163 (word-indexed from D100)
        private readonly byte[] _finsDm = new byte[256]; // D100..D163
        private bool _s7M10Bit0 = true;
        private bool _mcM10 = true;
        private bool _mcX1A = true;
        private bool _finsWr0Bit0 = true;

        private long _txId = 1;
        private ushort _s7PduRef = 1;
        private byte _finsSid = 1;

        public SimulatedPlcEngine()
        {
            SeedInitialMemory();
        }

        public bool IsConnected { get; private set; }
        public PlcBrandKind ActiveBrand { get; private set; } = PlcBrandKind.SiemensS7;

        public PlcTransactionRecord Handshake(PlcBrandKind brand)
        {
            ActiveBrand = brand;
            IsConnected = true;

            byte[] req;
            byte[] resp;
            IReadOnlyList<PlcFrameSegment> reqSegs;
            IReadOnlyList<PlcFrameSegment> respSegs;
            string detail;

            switch (brand)
            {
                case PlcBrandKind.SiemensS7:
                    req = PlcFrameCodec.BuildS7ConnectRequest(rack: 0, slot: 1);
                    resp = PlcFrameCodec.BuildS7ConnectResponse(rack: 0, slot: 1);
                    reqSegs = PlcFrameCodec.DissectS7Frame(req, false);
                    respSegs = PlcFrameCodec.DissectS7Frame(resp, true);
                    detail = "COTP CR/CC 建连成功 (Rack=0, Slot=1 · PDU=240B)";
                    break;

                case PlcBrandKind.MitsubishiMc3E:
                    // MC 3E has no extra app handshake; execute initial D100 ping read
                    req = PlcFrameCodec.BuildMc3EReadRequest(0xA8, 100, 1);
                    resp = PlcFrameCodec.BuildMc3EResponse(0x0000, _mcD.AsSpan(0, 2));
                    reqSegs = PlcFrameCodec.DissectMc3EFrame(req, false);
                    respSegs = PlcFrameCodec.DissectMc3EFrame(resp, true);
                    detail = "SLMP 3E 二进制直连就绪 (Port 5000 · 小端序)";
                    break;

                default:
                    req = PlcFrameCodec.BuildFinsHandshakePacket(clientNode: 0x0A);
                    resp = PlcFrameCodec.BuildFinsHandshakeResponse(clientNode: 0x0A, serverNode: 0x01);
                    reqSegs = PlcFrameCodec.DissectFinsFrame(req, false);
                    respSegs = PlcFrameCodec.DissectFinsFrame(resp, true);
                    detail = "FINS/TCP 节点握手成功 (PC SA1=0x0A ⇄ PLC DA1=0x01)";
                    break;
            }

            return new PlcTransactionRecord(
                _txId++, DateTime.Now, brand, "CONNECT", "Handshake", true, "OK", detail,
                req, resp, reqSegs, respSegs);
        }

        public void Disconnect() => IsConnected = false;

        public IReadOnlyList<PlcMemoryTagSnapshot> GetMemorySnapshot(PlcBrandKind brand) => brand switch
        {
            PlcBrandKind.SiemensS7 =>
            [
                new("DB1.DBX0.0", "产线运行使能", PlcDataType.Bool, 0, $"{_s7Db1[0]:X2}", ((_s7Db1[0] & 1) != 0).ToString(), "Bit 位偏移 0.0"),
                new("DB1.DBW2", "传送带速度 (×0.1)", PlcDataType.Int16, 2, PlcFrameCodec.ToHex(_s7Db1.AsSpan(2, 2)),
                    BinaryPrimitives.ReadInt16BigEndian(_s7Db1.AsSpan(2, 2)).ToString(CultureInfo.InvariantCulture), "Big-Endian 16位整数"),
                new("DB1.DBD4", "实测炉温 (°C)", PlcDataType.Float32, 4, PlcFrameCodec.ToHex(_s7Db1.AsSpan(4, 4)),
                    $"{BinaryPrimitives.ReadSingleBigEndian(_s7Db1.AsSpan(4, 4)):F2} °C", "Big-Endian IEEE-754 ABCD"),
                new("DB1.DBB8", "当前配方号 (S7String)", PlcDataType.S7String, 8, PlcFrameCodec.ToHex(_s7Db1.AsSpan(8, 12), 12),
                    ReadS7String(_s7Db1.AsSpan(8, 22)), $"头字节 Max={_s7Db1[8]}, Act={_s7Db1[9]}"),
                new("M10.0", "安全门闭合标志", PlcDataType.Bool, 10, _s7M10Bit0 ? "01" : "00", _s7M10Bit0.ToString(), "M 区位存储 (Area 0x83)"),
            ],
            PlcBrandKind.MitsubishiMc3E =>
            [
                new("D100", "产能计数 (Int16)", PlcDataType.Int16, 0, PlcFrameCodec.ToHex(_mcD.AsSpan(0, 2)),
                    BinaryPrimitives.ReadInt16LittleEndian(_mcD.AsSpan(0, 2)).ToString(CultureInfo.InvariantCulture), "十进制 D 区 · 小端序"),
                new("D102", "实测炉温 (Float32)", PlcDataType.Float32, 4, PlcFrameCodec.ToHex(_mcD.AsSpan(4, 4)),
                    $"{BinaryPrimitives.ReadSingleLittleEndian(_mcD.AsSpan(4, 4)):F2} °C", "双字 D102-D103 小端浮点"),
                new("D104", "伺服脉冲位置 (Int32)", PlcDataType.Int32, 8, PlcFrameCodec.ToHex(_mcD.AsSpan(8, 4)),
                    BinaryPrimitives.ReadInt32LittleEndian(_mcD.AsSpan(8, 4)).ToString(CultureInfo.InvariantCulture), "双字 D104-D105"),
                new("M10", "产线自动模式 (Bit)", PlcDataType.Bool, 10, _mcM10 ? "10" : "00", _mcM10.ToString(), "内部继电器 (Dev 0x90)"),
                new("X1A", "安全光幕输入 (Hex)", PlcDataType.Bool, 0x1A, _mcX1A ? "10" : "00", _mcX1A.ToString(), "X/Y 按十六进制 0x1A 寻址"),
            ],
            _ =>
            [
                new("D100", "设定速度 (Int16)", PlcDataType.Int16, 0, PlcFrameCodec.ToHex(_finsDm.AsSpan(0, 2)),
                    BinaryPrimitives.ReadInt16BigEndian(_finsDm.AsSpan(0, 2)).ToString(CultureInfo.InvariantCulture), "DM 区 (0x82) Word 大端"),
                new("D102", "实测炉温 (CDAB Float)", PlcDataType.Float32, 4, PlcFrameCodec.ToHex(_finsDm.AsSpan(4, 4)),
                    $"{ReadFloatCdab(_finsDm.AsSpan(4, 4)):F2} °C", "DM102-103 欧姆龙字交换 CDAB"),
                new("D104", "产能累加 (Int32)", PlcDataType.Int32, 8, PlcFrameCodec.ToHex(_finsDm.AsSpan(8, 4)),
                    BinaryPrimitives.ReadInt32BigEndian(_finsDm.AsSpan(8, 4)).ToString(CultureInfo.InvariantCulture), "DM104-105 双字"),
                new("W0.00", "运行启动继电器", PlcDataType.Bool, 0, _finsWr0Bit0 ? "01" : "00", _finsWr0Bit0.ToString(), "WR 位区 (Area 0x31)"),
            ],
        };

        public PlcTransactionRecord ExecuteRead(PlcBrandKind brand, string address, PlcDataType dataType) => brand switch
        {
            PlcBrandKind.SiemensS7 => ExecuteS7Read(address, dataType),
            PlcBrandKind.MitsubishiMc3E => ExecuteMcRead(address, dataType),
            _ => ExecuteFinsRead(address, dataType),
        };

        public PlcTransactionRecord ExecuteWrite(PlcBrandKind brand, string address, PlcDataType dataType, string valueText) => brand switch
        {
            PlcBrandKind.SiemensS7 => ExecuteS7Write(address, dataType, valueText),
            PlcBrandKind.MitsubishiMc3E => ExecuteMcWrite(address, dataType, valueText),
            _ => ExecuteFinsWrite(address, dataType, valueText),
        };

        public PlcTransactionRecord ExecuteFaultDemo(PlcBrandKind brand)
        {
            switch (brand)
            {
                case PlcBrandKind.SiemensS7:
                    var s7Req = PlcFrameCodec.BuildS7ReadRequest(_s7PduRef, 0x84, 99, 500, 0, 4);
                    var s7Resp = PlcFrameCodec.BuildS7ReadResponse(_s7PduRef++, 0x05, []);
                    return new PlcTransactionRecord(_txId++, DateTime.Now, brand, "READ (FAULT)", "DB99.DBD500", false,
                        "S7 Err 0x05 (地址越界/优化块拒绝)", "请检查 DB 编号并取消勾选“优化的块访问”",
                        s7Req, s7Resp, PlcFrameCodec.DissectS7Frame(s7Req, false), PlcFrameCodec.DissectS7Frame(s7Resp, true));

                case PlcBrandKind.MitsubishiMc3E:
                    var mcReq = PlcFrameCodec.BuildMc3EReadRequest(0xA8, 99999, 2);
                    var mcResp = PlcFrameCodec.BuildMc3EResponse(0xC059, []);
                    return new PlcTransactionRecord(_txId++, DateTime.Now, brand, "READ (FAULT)", "D99999", false,
                        "MC EndCode 0xC059 (命令/地址错误)", "超出软元件有效范围或子命令不匹配",
                        mcReq, mcResp, PlcFrameCodec.DissectMc3EFrame(mcReq, false), PlcFrameCodec.DissectMc3EFrame(mcResp, true));

                default:
                    var sid = _finsSid++;
                    var fReq = PlcFrameCodec.BuildFinsReadRequest(0x82, 60000, 0, 2, sid);
                    var fResp = PlcFrameCodec.BuildFinsResponse(0x01, 0x01, 0x1101, [], sid);
                    return new PlcTransactionRecord(_txId++, DateTime.Now, brand, "READ (FAULT)", "D60000", false,
                        "FINS EndCode 0x1101 (区域/地址越界)", "目标 DM 地址超出 PLC 存储区范围",
                        fReq, fResp, PlcFrameCodec.DissectFinsFrame(fReq, false), PlcFrameCodec.DissectFinsFrame(fResp, true));
            }
        }

        private PlcTransactionRecord ExecuteS7Read(string address, PlcDataType dataType)
        {
            var addr = address.Trim().ToUpperInvariant();
            var pdu = _s7PduRef++;

            if (addr == "M10.0")
            {
                var req = PlcFrameCodec.BuildS7ReadRequest(pdu, 0x83, 0, 10, 0, 1);
                byte[] data = [_s7M10Bit0 ? (byte)1 : (byte)0];
                var resp = PlcFrameCodec.BuildS7ReadResponse(pdu, 0xFF, data);
                return MakeRecord(PlcBrandKind.SiemensS7, "READ", addr, true, "0xFF OK", _s7M10Bit0.ToString(), req, resp);
            }

            if (addr.StartsWith("DB1.", StringComparison.Ordinal))
            {
                var (offset, len) = ResolveS7Db1Span(addr, dataType);
                if (offset >= 0 && offset + len <= _s7Db1.Length)
                {
                    var req = PlcFrameCodec.BuildS7ReadRequest(pdu, 0x84, 1, offset, 0, (ushort)len);
                    var slice = _s7Db1.AsSpan(offset, len);
                    var resp = PlcFrameCodec.BuildS7ReadResponse(pdu, 0xFF, slice);
                    var decoded = DecodeS7Value(slice, dataType);
                    return MakeRecord(PlcBrandKind.SiemensS7, "READ", addr, true, "0xFF OK", decoded, req, resp);
                }
            }

            var errReq = PlcFrameCodec.BuildS7ReadRequest(pdu, 0x84, 1, 999, 0, 2);
            var errResp = PlcFrameCodec.BuildS7ReadResponse(pdu, 0x05, []);
            return MakeRecord(PlcBrandKind.SiemensS7, "READ", addr, false, "Err 0x05", "地址越界或格式不受支持", errReq, errResp);
        }

        private PlcTransactionRecord ExecuteS7Write(string address, PlcDataType dataType, string valueText)
        {
            var addr = address.Trim().ToUpperInvariant();
            var pdu = _s7PduRef++;

            if (addr == "M10.0" && bool.TryParse(valueText, out var mBool))
            {
                _s7M10Bit0 = mBool;
                byte[] payload = [mBool ? (byte)1 : (byte)0];
                var req = PlcFrameCodec.BuildS7WriteRequest(pdu, 0x83, 0, 10, 0, payload);
                var resp = PlcFrameCodec.BuildS7WriteResponse(pdu, 0xFF);
                return MakeRecord(PlcBrandKind.SiemensS7, "WRITE", addr, true, "0xFF OK", mBool.ToString(), req, resp);
            }

            if (addr.StartsWith("DB1.", StringComparison.Ordinal))
            {
                var (offset, len) = ResolveS7Db1Span(addr, dataType);
                if (offset >= 0 && offset + len <= _s7Db1.Length && TryEncodeS7Value(valueText, dataType, len, out var payload))
                {
                    payload.CopyTo(_s7Db1.AsSpan(offset, payload.Length));
                    var req = PlcFrameCodec.BuildS7WriteRequest(pdu, 0x84, 1, offset, 0, payload);
                    var resp = PlcFrameCodec.BuildS7WriteResponse(pdu, 0xFF);
                    return MakeRecord(PlcBrandKind.SiemensS7, "WRITE", addr, true, "0xFF OK", valueText.Trim(), req, resp);
                }
            }

            var errReq = PlcFrameCodec.BuildS7WriteRequest(pdu, 0x84, 1, 0, 0, [0x00]);
            var errResp = PlcFrameCodec.BuildS7WriteResponse(pdu, 0x05);
            return MakeRecord(PlcBrandKind.SiemensS7, "WRITE", addr, false, "Err 0x05", "写入值格式无效或地址越界", errReq, errResp);
        }

        private PlcTransactionRecord ExecuteMcRead(string address, PlcDataType dataType)
        {
            var addr = address.Trim().ToUpperInvariant();
            if (addr == "M10")
            {
                var req = PlcFrameCodec.BuildMc3EReadRequest(0x90, 10, 1, isBit: true);
                var resp = PlcFrameCodec.BuildMc3EResponse(0x0000, [_mcM10 ? (byte)0x10 : (byte)0x00]);
                return MakeRecord(PlcBrandKind.MitsubishiMc3E, "READ", addr, true, "0x0000 OK", _mcM10.ToString(), req, resp);
            }

            if (addr == "X1A")
            {
                var req = PlcFrameCodec.BuildMc3EReadRequest(0x9C, 0x1A, 1, isBit: true);
                var resp = PlcFrameCodec.BuildMc3EResponse(0x0000, [_mcX1A ? (byte)0x10 : (byte)0x00]);
                return MakeRecord(PlcBrandKind.MitsubishiMc3E, "READ", addr, true, "0x0000 OK", _mcX1A.ToString(), req, resp);
            }

            if (addr.StartsWith('D') && int.TryParse(addr[1..], out var dNum) && dNum is >= 100 and <= 220)
            {
                var words = (ushort)(dataType is PlcDataType.Float32 or PlcDataType.Int32 ? 2 : 1);
                var byteOffset = (dNum - 100) * 2;
                var slice = _mcD.AsSpan(byteOffset, words * 2);
                var req = PlcFrameCodec.BuildMc3EReadRequest(0xA8, dNum, words);
                var resp = PlcFrameCodec.BuildMc3EResponse(0x0000, slice);
                var decoded = dataType switch
                {
                    PlcDataType.Float32 => $"{BinaryPrimitives.ReadSingleLittleEndian(slice):F2} °C",
                    PlcDataType.Int32 => BinaryPrimitives.ReadInt32LittleEndian(slice).ToString(CultureInfo.InvariantCulture),
                    _ => BinaryPrimitives.ReadInt16LittleEndian(slice).ToString(CultureInfo.InvariantCulture),
                };
                return MakeRecord(PlcBrandKind.MitsubishiMc3E, "READ", addr, true, "0x0000 OK", decoded, req, resp);
            }

            var errReq = PlcFrameCodec.BuildMc3EReadRequest(0xA8, 99999, 1);
            var errResp = PlcFrameCodec.BuildMc3EResponse(0xC059, []);
            return MakeRecord(PlcBrandKind.MitsubishiMc3E, "READ", addr, false, "0xC059 ERR", "地址超出范围", errReq, errResp);
        }

        private PlcTransactionRecord ExecuteMcWrite(string address, PlcDataType dataType, string valueText)
        {
            var addr = address.Trim().ToUpperInvariant();
            if (addr == "M10" && bool.TryParse(valueText, out var bVal))
            {
                _mcM10 = bVal;
                byte[] p = [bVal ? (byte)0x10 : (byte)0x00];
                var req = PlcFrameCodec.BuildMc3EWriteRequest(0x90, 10, p, 1, isBit: true);
                var resp = PlcFrameCodec.BuildMc3EResponse(0x0000, []);
                return MakeRecord(PlcBrandKind.MitsubishiMc3E, "WRITE", addr, true, "0x0000 OK", bVal.ToString(), req, resp);
            }

            if (addr.StartsWith('D') && int.TryParse(addr[1..], out var dNum) && dNum is >= 100 and <= 220)
            {
                var byteOffset = (dNum - 100) * 2;
                byte[]? payload = null;
                ushort words = 1;

                if (dataType == PlcDataType.Float32 && float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv))
                {
                    payload = new byte[4];
                    BinaryPrimitives.WriteSingleLittleEndian(payload, fv);
                    words = 2;
                }
                else if (dataType == PlcDataType.Int32 && int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                {
                    payload = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(payload, iv);
                    words = 2;
                }
                else if (short.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sv))
                {
                    payload = new byte[2];
                    BinaryPrimitives.WriteInt16LittleEndian(payload, sv);
                }

                if (payload is not null)
                {
                    payload.CopyTo(_mcD.AsSpan(byteOffset, payload.Length));
                    var req = PlcFrameCodec.BuildMc3EWriteRequest(0xA8, dNum, payload, words);
                    var resp = PlcFrameCodec.BuildMc3EResponse(0x0000, []);
                    return MakeRecord(PlcBrandKind.MitsubishiMc3E, "WRITE", addr, true, "0x0000 OK", valueText.Trim(), req, resp);
                }
            }

            var errReq = PlcFrameCodec.BuildMc3EWriteRequest(0xA8, 100, [0, 0], 1);
            var errResp = PlcFrameCodec.BuildMc3EResponse(0xC059, []);
            return MakeRecord(PlcBrandKind.MitsubishiMc3E, "WRITE", addr, false, "0xC059 ERR", "写入值格式无效或不可写", errReq, errResp);
        }

        private PlcTransactionRecord ExecuteFinsRead(string address, PlcDataType dataType)
        {
            var addr = address.Trim().ToUpperInvariant();
            var sid = _finsSid++;

            if (addr == "W0.00")
            {
                var req = PlcFrameCodec.BuildFinsReadRequest(0x31, 0, 0, 1, sid);
                var resp = PlcFrameCodec.BuildFinsResponse(0x01, 0x01, 0x0000, [_finsWr0Bit0 ? (byte)1 : (byte)0], sid);
                return MakeRecord(PlcBrandKind.OmronFinsTcp, "READ", addr, true, "0x0000 OK", _finsWr0Bit0.ToString(), req, resp);
            }

            if (addr.StartsWith('D') && ushort.TryParse(addr[1..], out var dNum) && dNum is >= 100 and <= 220)
            {
                var words = (ushort)(dataType is PlcDataType.Float32 or PlcDataType.Int32 ? 2 : 1);
                var byteOffset = (dNum - 100) * 2;
                var slice = _finsDm.AsSpan(byteOffset, words * 2);
                var req = PlcFrameCodec.BuildFinsReadRequest(0x82, dNum, 0, words, sid);
                var resp = PlcFrameCodec.BuildFinsResponse(0x01, 0x01, 0x0000, slice, sid);
                var decoded = dataType switch
                {
                    PlcDataType.Float32 => $"{ReadFloatCdab(slice):F2} °C",
                    PlcDataType.Int32 => BinaryPrimitives.ReadInt32BigEndian(slice).ToString(CultureInfo.InvariantCulture),
                    _ => BinaryPrimitives.ReadInt16BigEndian(slice).ToString(CultureInfo.InvariantCulture),
                };
                return MakeRecord(PlcBrandKind.OmronFinsTcp, "READ", addr, true, "0x0000 OK", decoded, req, resp);
            }

            var errReq = PlcFrameCodec.BuildFinsReadRequest(0x82, 60000, 0, 1, sid);
            var errResp = PlcFrameCodec.BuildFinsResponse(0x01, 0x01, 0x1101, [], sid);
            return MakeRecord(PlcBrandKind.OmronFinsTcp, "READ", addr, false, "0x1101 ERR", "DM 地址越界", errReq, errResp);
        }

        private PlcTransactionRecord ExecuteFinsWrite(string address, PlcDataType dataType, string valueText)
        {
            var addr = address.Trim().ToUpperInvariant();
            var sid = _finsSid++;

            if (addr == "W0.00" && bool.TryParse(valueText, out var bVal))
            {
                _finsWr0Bit0 = bVal;
                byte[] p = [bVal ? (byte)1 : (byte)0];
                var req = PlcFrameCodec.BuildFinsWriteRequest(0x31, 0, 0, 1, p, sid);
                var resp = PlcFrameCodec.BuildFinsResponse(0x01, 0x02, 0x0000, [], sid);
                return MakeRecord(PlcBrandKind.OmronFinsTcp, "WRITE", addr, true, "0x0000 OK", bVal.ToString(), req, resp);
            }

            if (addr.StartsWith('D') && ushort.TryParse(addr[1..], out var dNum) && dNum is >= 100 and <= 220)
            {
                var byteOffset = (dNum - 100) * 2;
                byte[]? payload = null;
                ushort words = 1;

                if (dataType == PlcDataType.Float32 && float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv))
                {
                    payload = new byte[4];
                    WriteFloatCdab(payload, fv);
                    words = 2;
                }
                else if (dataType == PlcDataType.Int32 && int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                {
                    payload = new byte[4];
                    BinaryPrimitives.WriteInt32BigEndian(payload, iv);
                    words = 2;
                }
                else if (short.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sv))
                {
                    payload = new byte[2];
                    BinaryPrimitives.WriteInt16BigEndian(payload, sv);
                }

                if (payload is not null)
                {
                    payload.CopyTo(_finsDm.AsSpan(byteOffset, payload.Length));
                    var req = PlcFrameCodec.BuildFinsWriteRequest(0x82, dNum, 0, words, payload, sid);
                    var resp = PlcFrameCodec.BuildFinsResponse(0x01, 0x02, 0x0000, [], sid);
                    return MakeRecord(PlcBrandKind.OmronFinsTcp, "WRITE", addr, true, "0x0000 OK", valueText.Trim(), req, resp);
                }
            }

            var errReq = PlcFrameCodec.BuildFinsWriteRequest(0x82, 100, 0, 1, [0, 0], sid);
            var errResp = PlcFrameCodec.BuildFinsResponse(0x01, 0x02, 0x1101, [], sid);
            return MakeRecord(PlcBrandKind.OmronFinsTcp, "WRITE", addr, false, "0x1101 ERR", "写入值格式无效或地址越界", errReq, errResp);
        }

        private PlcTransactionRecord MakeRecord(
            PlcBrandKind brand, string op, string addr, bool ok, string status, string decoded, byte[] req, byte[] resp)
        {
            var reqSegs = brand switch
            {
                PlcBrandKind.SiemensS7 => PlcFrameCodec.DissectS7Frame(req, false),
                PlcBrandKind.MitsubishiMc3E => PlcFrameCodec.DissectMc3EFrame(req, false),
                _ => PlcFrameCodec.DissectFinsFrame(req, false),
            };
            var respSegs = brand switch
            {
                PlcBrandKind.SiemensS7 => PlcFrameCodec.DissectS7Frame(resp, true),
                PlcBrandKind.MitsubishiMc3E => PlcFrameCodec.DissectMc3EFrame(resp, true),
                _ => PlcFrameCodec.DissectFinsFrame(resp, true),
            };

            return new PlcTransactionRecord(_txId++, DateTime.Now, brand, op, addr, ok, status, decoded, req, resp, reqSegs, respSegs);
        }

        private void SeedInitialMemory()
        {
            // S7 DB1: [0]=Bool, [2..3]=Int16(1250), [4..7]=Float32(186.5 BE), [8..29]=S7String("PCB-A09")
            _s7Db1[0] = 0x01;
            BinaryPrimitives.WriteInt16BigEndian(_s7Db1.AsSpan(2, 2), 1250);
            BinaryPrimitives.WriteSingleBigEndian(_s7Db1.AsSpan(4, 4), 186.5f);
            WriteS7String(_s7Db1.AsSpan(8, 22), "PCB-A09", maxLen: 20);

            // MC D100..D105 (Little-Endian)
            BinaryPrimitives.WriteInt16LittleEndian(_mcD.AsSpan(0, 2), 4520);
            BinaryPrimitives.WriteSingleLittleEndian(_mcD.AsSpan(4, 4), 186.5f);
            BinaryPrimitives.WriteInt32LittleEndian(_mcD.AsSpan(8, 4), 128500);

            // FINS DM100..DM105 (Word Big-Endian, Float32 CDAB)
            BinaryPrimitives.WriteInt16BigEndian(_finsDm.AsSpan(0, 2), 1500);
            WriteFloatCdab(_finsDm.AsSpan(4, 4), 186.5f);
            BinaryPrimitives.WriteInt32BigEndian(_finsDm.AsSpan(8, 4), 8640);
        }

        private static (int Offset, int Len) ResolveS7Db1Span(string addr, PlcDataType dataType)
        {
            if (addr == "DB1.DBX0.0") return (0, 1);
            if (addr == "DB1.DBW2") return (2, 2);
            if (addr == "DB1.DBD4") return (4, 4);
            if (addr == "DB1.DBB8") return (8, 22);
            var digits = new string(addr.SkipWhile(c => c != '.').Skip(1).SkipWhile(char.IsLetter).TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(digits, out var off)) return (-1, 0);
            var len = dataType switch
            {
                PlcDataType.Bool => 1,
                PlcDataType.Int16 => 2,
                PlcDataType.S7String => 22,
                _ => 4,
            };
            return (off, len);
        }

        private static string DecodeS7Value(ReadOnlySpan<byte> slice, PlcDataType dt) => dt switch
        {
            PlcDataType.Bool => ((slice[0] & 1) != 0).ToString(),
            PlcDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(slice).ToString(CultureInfo.InvariantCulture),
            PlcDataType.Int32 => BinaryPrimitives.ReadInt32BigEndian(slice).ToString(CultureInfo.InvariantCulture),
            PlcDataType.Float32 => $"{BinaryPrimitives.ReadSingleBigEndian(slice):F2} °C",
            PlcDataType.S7String => ReadS7String(slice),
            _ => PlcFrameCodec.ToHex(slice),
        };

        private static bool TryEncodeS7Value(string text, PlcDataType dt, int len, out byte[] bytes)
        {
            bytes = new byte[len];
            var raw = (text ?? string.Empty).Trim();
            switch (dt)
            {
                case PlcDataType.Bool when bool.TryParse(raw, out var b):
                    bytes[0] = b ? (byte)1 : (byte)0;
                    return true;
                case PlcDataType.Int16 when short.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s):
                    BinaryPrimitives.WriteInt16BigEndian(bytes, s);
                    return true;
                case PlcDataType.Int32 when int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i):
                    BinaryPrimitives.WriteInt32BigEndian(bytes, i);
                    return true;
                case PlcDataType.Float32 when float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f):
                    BinaryPrimitives.WriteSingleBigEndian(bytes, f);
                    return true;
                case PlcDataType.S7String when raw.Length > 0:
                    WriteS7String(bytes, raw, maxLen: (byte)Math.Max(1, len - 2));
                    return true;
                default:
                    return false;
            }
        }

        private static void WriteS7String(Span<byte> dest, string text, byte maxLen)
        {
            dest.Clear();
            var ascii = Encoding.ASCII.GetBytes(text);
            var actLen = (byte)Math.Min(ascii.Length, Math.Min(maxLen, dest.Length - 2));
            dest[0] = maxLen;
            dest[1] = actLen;
            ascii.AsSpan(0, actLen).CopyTo(dest.Slice(2, actLen));
        }

        private static string ReadS7String(ReadOnlySpan<byte> src)
        {
            if (src.Length < 2) return string.Empty;
            var actLen = Math.Min(src[1], src.Length - 2);
            return Encoding.ASCII.GetString(src.Slice(2, actLen));
        }

        private static float ReadFloatCdab(ReadOnlySpan<byte> src)
        {
            Span<byte> abcd = [src[2], src[3], src[0], src[1]];
            return BinaryPrimitives.ReadSingleBigEndian(abcd);
        }

        private static void WriteFloatCdab(Span<byte> dest, float value)
        {
            Span<byte> abcd = stackalloc byte[4];
            BinaryPrimitives.WriteSingleBigEndian(abcd, value);
            dest[0] = abcd[2];
            dest[1] = abcd[3];
            dest[2] = abcd[0];
            dest[3] = abcd[1];
        }
    }
}
