using System.Buffers.Binary;

namespace LearnSmartManufacturing.Models.EtherCat
{
    /// <summary>EtherCAT State Machine (ESM) states per ETG.1000 §6.4.1.</summary>
    public enum EsmState : byte
    {
        Init = 0x01,
        PreOp = 0x02,
        SafeOp = 0x04,
        Op = 0x08,
    }

    /// <summary>EtherCAT datagram addressing / command codes per ETG.1000.4.</summary>
    public enum EtherCatCommand : byte
    {
        Aprd = 0x01, // Auto Increment Physical Read
        Apwr = 0x02, // Auto Increment Physical Write
        Brd = 0x07,  // Broadcast Read
        Bwr = 0x08,  // Broadcast Write
        Lrd = 0x0A,  // Logical Memory Read
        Lwr = 0x0B,  // Logical Memory Write
        Lrw = 0x0C,  // Logical Memory Read Write (standard cyclic PDO exchange)
        Armw = 0x0D, // Auto Increment Physical Read Multiple Write (DC sync)
    }

    /// <summary>CiA 402 Power Drive System (PDS) state machine states (CoE 0x6040/0x6041).</summary>
    public enum Cia402State
    {
        SwitchOnDisabled,
        ReadyToSwitchOn,
        SwitchedOn,
        OperationEnabled,
        Fault,
    }

    /// <summary>Fault injection scenarios for the simulated EtherCAT ring.</summary>
    public enum EtherCatFaultKind
    {
        None,
        CableBreakAtSlave2,
        SmWatchdogTimeout,
        DcSyncLost,
    }

    public sealed record EtherCatFrameSegment(string Label, string HexText, string Kind);

    public sealed record EtherCatFrameSnapshot(
        long CycleNumber,
        DateTime Timestamp,
        int CycleTimeUs,
        byte[] RawFrameBytes,
        uint LogicalAddress,
        int ProcessDataLength,
        ushort ExpectedWkc,
        ushort ActualWkc,
        double FrameLatencyUs,
        string FrameSummary,
        IReadOnlyList<EtherCatFrameSegment> Segments)
    {
        public bool IsWkcValid => ActualWkc == ExpectedWkc;
        public string WkcText => $"{ActualWkc} / {ExpectedWkc}";
    }

    public sealed record EtherCatSlaveSnapshot(
        int PositionIndex,
        ushort StationAddress,
        string Name,
        string Model,
        string Role,
        bool IsServoDrive,
        bool IsIoModule,
        bool IsReferenceClock,
        bool IsOnline,
        EsmState State,
        bool HasError,
        ushort AlStatusCode,
        string AlStatusText,
        int PropagationDelayNs,
        double DcOffsetNs,
        int RxPdoOffset,
        int RxPdoBytes,
        int TxPdoOffset,
        int TxPdoBytes,
        ushort ExpectedWkc,
        ushort ActualWkc,
        Cia402State Cia402State,
        ushort Controlword,
        ushort Statusword,
        sbyte ModeOfOperation,
        int TargetPosition,
        int ActualPosition,
        int ActualVelocity,
        short ActualTorquePermille,
        ushort DigitalInputs,
        ushort DigitalOutputs)
    {
        public string AddressHex => $"0x{StationAddress:X4}";
        public string StateTag => !IsOnline ? "OFFLINE" : HasError ? $"{EtherCatFrameCodec.FormatEsmName(State)}+ERR" : EtherCatFrameCodec.FormatEsmName(State);
        public string WkcTag => $"{ActualWkc}/{ExpectedWkc}";
        public string DcOffsetText => !IsOnline ? "--" : IsReferenceClock ? "0 ns (RefClk)" : $"{DcOffsetNs:+0.0;-0.0;0.0} ns";
        public string DelayText => $"{PropagationDelayNs} ns";

        public string PdoMappingSummary => RxPdoBytes == 0 && TxPdoBytes == 0
            ? "无 PDO (纯拓扑耦合器)"
            : $"FMMU @0x{RxPdoOffset:X2} · Rx {RxPdoBytes}B / Tx {TxPdoBytes}B";

        public string ProcessValueSummary
        {
            get
            {
                if (!IsOnline)
                {
                    return "链路断开（从站失联）";
                }

                if (IsServoDrive)
                {
                    var mode = ModeOfOperation switch
                    {
                        8 => "CSP",
                        9 => "CSV",
                        1 => "PP",
                        _ => $"M{ModeOfOperation}",
                    };
                    return $"{Cia402State} [{mode}] · Cmd={TargetPosition} / Act={ActualPosition} · Trq={ActualTorquePermille / 10.0:F1}%";
                }

                if (IsIoModule)
                {
                    return $"DI=0x{DigitalInputs:X4} ({Convert.ToString(DigitalInputs & 0x0F, 2).PadLeft(4, '0')}) · DO=0x{DigitalOutputs:X4} ({Convert.ToString(DigitalOutputs & 0x0F, 2).PadLeft(4, '0')})";
                }

                return "DC 参考时钟源 · E-Bus 电源正常";
            }
        }
    }

    public sealed record SdoEntry(
        ushort Index,
        byte SubIndex,
        string Name,
        string DataType,
        string Access,
        string ValueText,
        string Unit,
        string Description)
    {
        public string IndexText => $"0x{Index:X4}:{SubIndex:X2}";
        public bool IsWritable => string.Equals(Access, "RW", StringComparison.OrdinalIgnoreCase);
    }

    public sealed record SdoTransactionResult(
        bool Success,
        uint AbortCode,
        string AbortText,
        string ValueText,
        string Summary)
    {
        public string AbortCodeHex => $"0x{AbortCode:X8}";
    }

    public sealed record DcJitterSample(
        long CycleNumber,
        double Slave1OffsetNs,
        double Slave2OffsetNs,
        double Slave3OffsetNs,
        double MaxAbsOffsetNs,
        bool DcEnabled);

    public sealed record EtherCatLogEntry(DateTime Timestamp, string Level, string Tag, string Message)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
    }

    /// <summary>
    /// Wire-format EtherCAT (EtherType 0x88A4) frame codec, ESM transition validator,
    /// CiA 402 statusword decoder, and AL / SDO diagnostic code tables.
    /// </summary>
    public static class EtherCatFrameCodec
    {
        public const ushort EtherTypeEtherCat = 0x88A4;
        public const int EthernetHeaderLength = 14;
        public const int EcatHeaderLength = 2;
        public const int DatagramHeaderLength = 10;
        public const int WkcLength = 2;

        /// <summary>
        /// Builds a complete Ethernet II + EtherCAT (0x88A4) + single LRW datagram frame.
        /// </summary>
        public static byte[] BuildLrwEthernetFrame(byte datagramIndex, uint logicalAddress, ReadOnlySpan<byte> processData, ushort wkc)
        {
            var datagramLength = DatagramHeaderLength + processData.Length + WkcLength;
            var frame = new byte[EthernetHeaderLength + EcatHeaderLength + datagramLength];

            // Ethernet II Header: Broadcast Dst MAC (FF:FF:FF:FF:FF:FF), Master Src MAC (02:EC:A7:00:00:01), EtherType 0x88A4
            frame.AsSpan(0, 6).Fill(0xFF);
            frame[6] = 0x02;
            frame[7] = 0xEC;
            frame[8] = 0xA7;
            frame[9] = 0x00;
            frame[10] = 0x00;
            frame[11] = 0x01;
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12, 2), EtherTypeEtherCat);

            // EtherCAT Frame Header (2B LE): bits 0..10 = Length, bits 12..15 = Type (0x1 = EtherCAT command)
            var ecatHeader = (ushort)((datagramLength & 0x07FF) | 0x1000);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(14, 2), ecatHeader);

            // Datagram Header (10B LE)
            var dg = frame.AsSpan(16);
            dg[0] = (byte)EtherCatCommand.Lrw; // 0x0C
            dg[1] = datagramIndex;
            BinaryPrimitives.WriteUInt32LittleEndian(dg.Slice(2, 4), logicalAddress);
            var lenFlags = (ushort)(processData.Length & 0x07FF); // More=0, Circulating=0
            BinaryPrimitives.WriteUInt16LittleEndian(dg.Slice(6, 2), lenFlags);
            BinaryPrimitives.WriteUInt16LittleEndian(dg.Slice(8, 2), 0x0000); // IRQ

            // Process Data Payload (RxPDO/TxPDO combined logical image)
            processData.CopyTo(dg.Slice(10, processData.Length));

            // Working Counter (2B LE)
            BinaryPrimitives.WriteUInt16LittleEndian(dg.Slice(10 + processData.Length, 2), wkc);
            return frame;
        }

        /// <summary>Parses a raw Ethernet II + EtherCAT frame into its key wire fields.</summary>
        public static bool TryParseLrwEthernetFrame(
            ReadOnlySpan<byte> frame,
            out EtherCatCommand command,
            out byte datagramIndex,
            out uint logicalAddress,
            out int dataLength,
            out ushort wkc)
        {
            command = default;
            datagramIndex = 0;
            logicalAddress = 0;
            dataLength = 0;
            wkc = 0;

            if (frame.Length < EthernetHeaderLength + EcatHeaderLength + DatagramHeaderLength + WkcLength)
            {
                return false;
            }

            var etherType = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(12, 2));
            if (etherType != EtherTypeEtherCat)
            {
                return false;
            }

            var ecatHeader = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(14, 2));
            var ecatType = (ecatHeader >> 12) & 0x0F;
            if (ecatType != 0x1)
            {
                return false;
            }

            var dg = frame.Slice(16);
            command = (EtherCatCommand)dg[0];
            datagramIndex = dg[1];
            logicalAddress = BinaryPrimitives.ReadUInt32LittleEndian(dg.Slice(2, 4));
            dataLength = BinaryPrimitives.ReadUInt16LittleEndian(dg.Slice(6, 2)) & 0x07FF;

            if (dg.Length < DatagramHeaderLength + dataLength + WkcLength)
            {
                return false;
            }

            wkc = BinaryPrimitives.ReadUInt16LittleEndian(dg.Slice(DatagramHeaderLength + dataLength, 2));
            return true;
        }

        /// <summary>Produces color-coded frame segments for the EtherCAT wire-level frame dissector.</summary>
        public static IReadOnlyList<EtherCatFrameSegment> DissectEthernetFrame(ReadOnlySpan<byte> frame, ushort expectedWkc)
        {
            if (!TryParseLrwEthernetFrame(frame, out var cmd, out var idx, out var addr, out var dataLen, out var wkc))
            {
                return [];
            }

            var segments = new List<EtherCatFrameSegment>
            {
                new("Eth II · 0x88A4", "FF..FF | 88 A4", "Eth"),
                new("ECAT Hdr", ToHex(frame.Slice(14, 2)), "Ecat"),
                new($"Cmd {cmd.ToString().ToUpperInvariant()} (0x{(byte)cmd:X2})", $"Idx={idx:X2} @0x{addr:X8}", "Cmd"),
                new($"FMMU PDO ({dataLen}B)", ToHex(frame.Slice(26, Math.Min(dataLen, 8))) + (dataLen > 8 ? " …" : string.Empty), "Pdo"),
                new(wkc == expectedWkc ? $"WKC {wkc}/{expectedWkc} OK" : $"WKC {wkc}/{expectedWkc} ERR",
                    ToHex(frame.Slice(26 + dataLen, 2)),
                    wkc == expectedWkc ? "WkcOk" : "WkcErr"),
            };

            return segments;
        }

        /// <summary>
        /// Validates EtherCAT State Machine (ESM) transitions per ETG.1000 §6.4.1.
        /// Direct upward jumps (e.g., INIT→SAFE-OP, INIT→OP, PRE-OP→OP) are illegal.
        /// </summary>
        public static bool IsValidEsmTransition(EsmState from, EsmState to, out ushort alStatusCode, out string reason)
        {
            if (from == to)
            {
                alStatusCode = 0x0000;
                reason = $"当前已处于 {FormatEsmName(to)} 状态。";
                return true;
            }

            var allowed = (from, to) switch
            {
                (EsmState.Init, EsmState.PreOp) => true,
                (EsmState.PreOp, EsmState.Init) => true,
                (EsmState.PreOp, EsmState.SafeOp) => true,
                (EsmState.SafeOp, EsmState.Init) => true,
                (EsmState.SafeOp, EsmState.PreOp) => true,
                (EsmState.SafeOp, EsmState.Op) => true,
                (EsmState.Op, EsmState.Init) => true,
                (EsmState.Op, EsmState.PreOp) => true,
                (EsmState.Op, EsmState.SafeOp) => true,
                _ => false,
            };

            if (allowed)
            {
                alStatusCode = 0x0000;
                reason = $"{FormatEsmName(from)} → {FormatEsmName(to)} 合法切换。";
                return true;
            }

            alStatusCode = 0x0011; // Invalid requested state change
            reason = $"ETG.1000 禁止跨级升态 {FormatEsmName(from)} → {FormatEsmName(to)} (AL 0x0011)，须按 INIT→PRE-OP→SAFE-OP→OP 顺序切换。";
            return false;
        }

        public static string FormatEsmName(EsmState state) => state switch
        {
            EsmState.Init => "INIT",
            EsmState.PreOp => "PRE-OP",
            EsmState.SafeOp => "SAFE-OP",
            EsmState.Op => "OP",
            _ => state.ToString(),
        };

        /// <summary>Returns exact CiA 402 statusword bit pattern (0x6041) for a drive state.</summary>
        public static ushort BuildCia402Statusword(Cia402State state) => state switch
        {
            Cia402State.SwitchOnDisabled => 0x0040, // Bit 6
            Cia402State.ReadyToSwitchOn => 0x0021,  // Bits 5, 0
            Cia402State.SwitchedOn => 0x0023,       // Bits 5, 1, 0
            Cia402State.OperationEnabled => 0x0027, // Bits 5, 2, 1, 0
            Cia402State.Fault => 0x0008,            // Bit 3
            _ => 0x0040,
        };

        /// <summary>Decodes CiA 402 statusword (0x6041) per IEC 61800-7-201 state mask.</summary>
        public static Cia402State DecodeCia402Statusword(ushort statusword)
        {
            if ((statusword & 0x004F) == 0x0008)
            {
                return Cia402State.Fault;
            }

            if ((statusword & 0x004F) == 0x0040)
            {
                return Cia402State.SwitchOnDisabled;
            }

            return (statusword & 0x006F) switch
            {
                0x0021 => Cia402State.ReadyToSwitchOn,
                0x0023 => Cia402State.SwitchedOn,
                0x0027 => Cia402State.OperationEnabled,
                _ => Cia402State.SwitchOnDisabled,
            };
        }

        public static string FormatAlStatusCode(ushort code) => code switch
        {
            0x0000 => "No Error",
            0x0011 => "Invalid Requested State Change",
            0x001A => "Synchronization Error",
            0x001B => "Sync Manager Watchdog Timeout",
            0x002D => "No SYNC0 Received (DC Lost)",
            0x0032 => "PLL Error",
            _ => $"AL Error 0x{code:X4}",
        };

        public static string FormatSdoAbortCode(uint abortCode) => abortCode switch
        {
            0x00000000 => "OK",
            0x06010002 => "Attempt to write a read-only object (对象只读)",
            0x06020000 => "Object does not exist in Object Dictionary (索引不存在)",
            0x06090011 => "Sub-index does not exist (子索引不存在)",
            0x06090030 => "Value range of parameter exceeded (数值越界)",
            0x08000022 => "Data cannot be transferred in present state (INIT 态禁用 SDO 邮箱)",
            _ => $"SDO Abort 0x{abortCode:X8}",
        };

        public static string ToHex(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            return string.Join(' ', bytes.ToArray().Select(b => b.ToString("X2")));
        }
    }
}
