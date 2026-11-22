using System.Buffers.Binary;
using System.Text;
using LearnSmartManufacturing.Models.Plc;
using LearnSmartManufacturing.Services.Plc;

namespace LearnSmartManufacturing.Tests.Models
{
    public class PlcProtocolTests
    {
        [Fact]
        public void S7ConnectAndReadWriteFrames_EncodeExpectedHeadersAndDissectCleanly()
        {
            var connReq = PlcFrameCodec.BuildS7ConnectRequest(rack: 0, slot: 1);
            var connResp = PlcFrameCodec.BuildS7ConnectResponse(rack: 0, slot: 1);

            Assert.Equal(22, connReq.Length);
            Assert.Equal(0x03, connReq[0]);
            Assert.Equal(0xE0, connReq[5]);
            Assert.Equal(0x01, connReq[18]); // Rack 0, Slot 1
            Assert.Equal(0xD0, connResp[5]);
            Assert.Equal(3, PlcFrameCodec.DissectS7Frame(connReq, false).Count);
            Assert.Equal(3, PlcFrameCodec.DissectS7Frame(connResp, true).Count);

            var readReq = PlcFrameCodec.BuildS7ReadRequest(pduRef: 7, areaCode: 0x84, dbNumber: 1, byteOffset: 4, bitOffset: 0, readLength: 4);
            Assert.Equal(31, readReq.Length);
            Assert.Equal(0x32, readReq[7]);
            Assert.Equal(0x04, readReq[17]); // Read Var
            Assert.Equal(0x84, readReq[27]); // DB area
            Assert.Equal(32, (readReq[28] << 16) | (readReq[29] << 8) | readReq[30]); // byteOffset 4 * 8 bits

            var errResp = PlcFrameCodec.BuildS7ReadResponse(7, 0x05, []);
            var errSegs = PlcFrameCodec.DissectS7Frame(errResp, true);
            Assert.Contains(errSegs, s => s.Kind == "Err" && s.Label.Contains("0x05", StringComparison.Ordinal));
        }

        [Fact]
        public void Mc3EFrames_EncodeLittleEndianSubheadersAndEndCodes()
        {
            var req = PlcFrameCodec.BuildMc3EReadRequest(deviceCode: 0xA8, headDevice: 102, wordCount: 2);
            Assert.Equal(21, req.Length);
            Assert.Equal(0x50, req[0]);
            Assert.Equal(0x00, req[1]);
            Assert.Equal(0x0401, BinaryPrimitives.ReadUInt16LittleEndian(req.AsSpan(11, 2)));
            Assert.Equal(102, req[15] | (req[16] << 8) | (req[17] << 16));
            Assert.Equal(0xA8, req[18]);

            var okResp = PlcFrameCodec.BuildMc3EResponse(0x0000, [0x00, 0x80, 0x3A, 0x43]);
            Assert.Equal(0xD0, okResp[0]);
            Assert.Contains(PlcFrameCodec.DissectMc3EFrame(okResp, true), s => s.Kind == "Ok");

            var errResp = PlcFrameCodec.BuildMc3EResponse(0xC059, []);
            Assert.Contains(PlcFrameCodec.DissectMc3EFrame(errResp, true), s => s.Kind == "Err" && s.Label.Contains("C059", StringComparison.Ordinal));
        }

        [Fact]
        public void OmronFinsFrames_EncodeAsciiHeaderHandshakeAndMemoryCommands()
        {
            var hsReq = PlcFrameCodec.BuildFinsHandshakePacket(clientNode: 0x0A);
            var hsResp = PlcFrameCodec.BuildFinsHandshakeResponse(clientNode: 0x0A, serverNode: 0x01);

            Assert.Equal("FINS", Encoding.ASCII.GetString(hsReq, 0, 4));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(hsReq.AsSpan(8, 4)));
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(hsResp.AsSpan(8, 4)));
            Assert.Equal(0x01u, BinaryPrimitives.ReadUInt32BigEndian(hsResp.AsSpan(20, 4)));

            var readReq = PlcFrameCodec.BuildFinsReadRequest(areaCode: 0x82, startAddr: 102, bitOffset: 0, wordCount: 2, sid: 5);
            Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(readReq.AsSpan(8, 4)));
            Assert.Equal(0x01, readReq[26]);
            Assert.Equal(0x01, readReq[27]);
            Assert.Equal(0x82, readReq[28]);

            var errResp = PlcFrameCodec.BuildFinsResponse(0x01, 0x01, 0x1101, [], sid: 5);
            Assert.Contains(PlcFrameCodec.DissectFinsFrame(errResp, true), s => s.Kind == "Err" && s.Label.Contains("1101", StringComparison.Ordinal));
        }

        [Fact]
        public void SimulatedPlcEngine_PreservesBrandSpecificByteOrdersAndS7StringHeader()
        {
            var engine = new SimulatedPlcEngine();

            // Verify 186.5f byte representations across all 3 brands:
            // S7 Big-Endian ABCD: 43 3A 80 00
            var s7Snap = engine.GetMemorySnapshot(PlcBrandKind.SiemensS7);
            Assert.Equal("43 3A 80 00", s7Snap.Single(t => t.Address == "DB1.DBD4").RawHex);
            Assert.StartsWith("14 07 50 43 42 2D 41 30 39", s7Snap.Single(t => t.Address == "DB1.DBB8").RawHex, StringComparison.Ordinal);

            // MC 3E Little-Endian DCBA: 00 80 3A 43
            var mcSnap = engine.GetMemorySnapshot(PlcBrandKind.MitsubishiMc3E);
            Assert.Equal("00 80 3A 43", mcSnap.Single(t => t.Address == "D102").RawHex);

            // Omron FINS Word-Swap CDAB: 80 00 43 3A
            var finsSnap = engine.GetMemorySnapshot(PlcBrandKind.OmronFinsTcp);
            Assert.Equal("80 00 43 3A", finsSnap.Single(t => t.Address == "D102").RawHex);

            // S7 S7String write/read roundtrip
            var writeStr = engine.ExecuteWrite(PlcBrandKind.SiemensS7, "DB1.DBB8", PlcDataType.S7String, "LOT-99");
            Assert.True(writeStr.IsSuccess);
            var readStr = engine.ExecuteRead(PlcBrandKind.SiemensS7, "DB1.DBB8", PlcDataType.S7String);
            Assert.True(readStr.IsSuccess);
            Assert.Equal("LOT-99", readStr.DecodedValueText);

            // MC X1A hex input is readable but not writable
            var readX1A = engine.ExecuteRead(PlcBrandKind.MitsubishiMc3E, "X1A", PlcDataType.Bool);
            Assert.True(readX1A.IsSuccess);
            var writeX1A = engine.ExecuteWrite(PlcBrandKind.MitsubishiMc3E, "X1A", PlcDataType.Bool, "False");
            Assert.False(writeX1A.IsSuccess);
            Assert.Contains("C059", writeX1A.StatusCodeText, StringComparison.Ordinal);

            // Omron FINS CDAB float write/read roundtrip
            var writeFins = engine.ExecuteWrite(PlcBrandKind.OmronFinsTcp, "D102", PlcDataType.Float32, "215.25");
            Assert.True(writeFins.IsSuccess);
            var readFins = engine.ExecuteRead(PlcBrandKind.OmronFinsTcp, "D102", PlcDataType.Float32);
            Assert.True(readFins.IsSuccess);
            Assert.Equal("215.25 °C", readFins.DecodedValueText);

            // Fault demos return authentic brand error codes
            Assert.False(engine.ExecuteFaultDemo(PlcBrandKind.SiemensS7).IsSuccess);
            Assert.False(engine.ExecuteFaultDemo(PlcBrandKind.MitsubishiMc3E).IsSuccess);
            Assert.False(engine.ExecuteFaultDemo(PlcBrandKind.OmronFinsTcp).IsSuccess);
        }

        [Fact]
        public void EvaluateBatchOptimization_ReducesRoundTripsAndSpeedsUpPolling()
        {
            var tightGap = PlcFrameCodec.EvaluateBatchOptimization(tagCount: 40, maxGapBytes: 0, pduLimitBytes: 240, rttMs: 3.0);
            var mergedGap = PlcFrameCodec.EvaluateBatchOptimization(tagCount: 40, maxGapBytes: 16, pduLimitBytes: 240, rttMs: 3.0);

            Assert.Equal(40, mergedGap.NaiveRoundTrips);
            Assert.True(mergedGap.BatchedRoundTrips < tightGap.BatchedRoundTrips);
            Assert.True(mergedGap.SpeedupFactor >= 10.0);
            Assert.Equal(mergedGap.BatchedRoundTrips, mergedGap.BatchSummaries.Count);
        }
    }
}
