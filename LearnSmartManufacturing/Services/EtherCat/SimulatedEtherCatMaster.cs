using System.Buffers.Binary;
using System.Globalization;
using LearnSmartManufacturing.Models.EtherCat;

namespace LearnSmartManufacturing.Services.EtherCat
{
    /// <summary>
    /// Full-fidelity EtherCAT Master &amp; 4-Slave Ring Simulator implementing:
    /// - ETG.1000 EtherCAT State Machine (INIT / PRE-OP / SAFE-OP / OP) with strict transition checks
    /// - FMMU logical process image + real 0x88A4 LRW frame generation &amp; Working Counter (WKC) verification
    /// - CoE (CANopen over EtherCAT) SDO mailbox with CiA 402 Object Dictionary &amp; standard Abort Codes
    /// - Distributed Clocks (DC) propagation delay &amp; SYNC0 phase jitter model
    /// - Daisy-chain cable break, SM Watchdog (0x001B), and DC Sync Lost (0x002D) fault injection
    /// </summary>
    public sealed class SimulatedEtherCatMaster
    {
        private sealed class SlaveNode
        {
            public required int PositionIndex { get; init; }
            public required ushort StationAddress { get; init; }
            public required string Name { get; init; }
            public required string Model { get; init; }
            public required string Role { get; init; }
            public required bool IsServoDrive { get; init; }
            public required bool IsIoModule { get; init; }
            public required bool IsReferenceClock { get; init; }
            public required int PropagationDelayNs { get; init; }
            public required int RxPdoOffset { get; init; }
            public required int RxPdoBytes { get; init; }
            public required int TxPdoOffset { get; init; }
            public required int TxPdoBytes { get; init; }

            public bool IsOnline { get; set; } = true;
            public EsmState State { get; set; } = EsmState.Init;
            public bool HasError { get; set; }
            public ushort AlStatusCode { get; set; }
            public double DcOffsetNs { get; set; }

            // CiA 402 Servo state
            public Cia402State Cia402State { get; set; } = Cia402State.SwitchOnDisabled;
            public ushort Controlword { get; set; }
            public sbyte ModeOfOperation { get; set; } = 8; // 8 = CSP
            public int TargetPosition { get; set; }
            public int ActualPosition { get; set; }
            public int ActualVelocity { get; set; }
            public short ActualTorquePermille { get; set; }
            public uint FollowingErrorWindow { get; set; } = 5000;
            public uint ProfileVelocity { get; set; } = 8000;

            // Digital I/O state
            public ushort DigitalInputs { get; set; } = 0x0005; // Safety door + Air pressure OK
            public ushort DigitalOutputs { get; set; }
        }

        private const uint LogicalBaseAddress = 0x0100_0000;
        private const int ProcessImageLength = 18; // Slave1(8B) + Slave2(8B) + Slave3(2B)

        private readonly List<SlaveNode> _slaves;
        private readonly Random _rng = new(4021);
        private byte _datagramIndex;

        public SimulatedEtherCatMaster()
        {
            _slaves =
            [
                new SlaveNode
                {
                    PositionIndex = 0, StationAddress = 0x1001, Name = "EK1100", Model = "EK1100 Bus Coupler",
                    Role = "DC 参考时钟 / 耦合器", IsServoDrive = false, IsIoModule = false, IsReferenceClock = true,
                    PropagationDelayNs = 0, RxPdoOffset = 0, RxPdoBytes = 0, TxPdoOffset = 0, TxPdoBytes = 0,
                },
                new SlaveNode
                {
                    PositionIndex = 1, StationAddress = 0x1002, Name = "IS620N-X", Model = "Inovance IS620N (X轴)",
                    Role = "CiA 402 X 轴伺服", IsServoDrive = true, IsIoModule = false, IsReferenceClock = false,
                    PropagationDelayNs = 145, RxPdoOffset = 0, RxPdoBytes = 8, TxPdoOffset = 0, TxPdoBytes = 8,
                },
                new SlaveNode
                {
                    PositionIndex = 2, StationAddress = 0x1003, Name = "IS620N-Y", Model = "Inovance IS620N (Y轴)",
                    Role = "CiA 402 Y 轴伺服", IsServoDrive = true, IsIoModule = false, IsReferenceClock = false,
                    PropagationDelayNs = 290, RxPdoOffset = 8, RxPdoBytes = 8, TxPdoOffset = 8, TxPdoBytes = 8,
                },
                new SlaveNode
                {
                    PositionIndex = 3, StationAddress = 0x1004, Name = "EL1809/2809", Model = "16-Ch DI/DO Module",
                    Role = "高速数字量 I/O", IsServoDrive = false, IsIoModule = true, IsReferenceClock = false,
                    PropagationDelayNs = 435, RxPdoOffset = 16, RxPdoBytes = 2, TxPdoOffset = 16, TxPdoBytes = 2,
                },
            ];
        }

        public EsmState MasterState { get; private set; } = EsmState.Init;
        public int CycleTimeUs { get; set; } = 1000;
        public bool DcEnabled { get; set; } = true;
        public bool SimulateOsJitter { get; set; }
        public bool CspSineTrajectoryEnabled { get; set; } = true;
        public EtherCatFaultKind ActiveFault { get; private set; } = EtherCatFaultKind.None;
        public long CycleCount { get; private set; }
        public long WkcErrorCount { get; private set; }

        public event Action<EtherCatLogEntry>? Logged;

        public bool IsMailboxAllowed => MasterState is EsmState.PreOp or EsmState.SafeOp or EsmState.Op;
        public bool IsInputsActive => MasterState is EsmState.SafeOp or EsmState.Op;
        public bool IsOutputsActive => MasterState == EsmState.Op;

        public IReadOnlyList<EtherCatSlaveSnapshot> GetSlaveSnapshots() =>
            [.. _slaves.Select(BuildSnapshot)];

        public bool RequestState(EsmState target, out ushort alStatusCode, out string message)
        {
            if (ActiveFault == EtherCatFaultKind.CableBreakAtSlave2 && target > MasterState)
            {
                alStatusCode = 0x001A;
                message = "链路断线故障未清除（Slave 2/3 失联），禁止继续升态。";
                Log("Error", "ESM", message);
                return false;
            }

            if (!EtherCatFrameCodec.IsValidEsmTransition(MasterState, target, out alStatusCode, out message))
            {
                Log("Warn", "ESM", message);
                return false;
            }

            var prev = MasterState;
            MasterState = target;
            foreach (var s in _slaves.Where(x => x.IsOnline))
            {
                s.State = target;
                if (target == EsmState.Init)
                {
                    s.HasError = false;
                    s.AlStatusCode = 0x0000;
                    s.Controlword = 0x0000;
                    s.Cia402State = Cia402State.SwitchOnDisabled;
                    s.ActualVelocity = 0;
                    s.ActualTorquePermille = 0;
                }
                else if (target is EsmState.PreOp or EsmState.SafeOp && s.IsServoDrive)
                {
                    // Outputs enter safe state outside OP
                    s.ActualVelocity = 0;
                    s.ActualTorquePermille = 0;
                }
            }

            var capability = target switch
            {
                EsmState.Init => "邮箱 SDO 禁用 · PDO 禁用（主站仅读写 ESC 寄存器）",
                EsmState.PreOp => "邮箱 SDO 已就绪（可配置 PDO 映射与 DC）· PDO 未激活",
                EsmState.SafeOp => "输入 TxPDO 激活（可采集反馈）· 输出 RxPDO 锁定在安全态",
                EsmState.Op => "TxPDO + RxPDO 全双工实时交换激活（预期 LRW WKC=9）",
                _ => string.Empty,
            };

            message = $"ESM 状态切换 {EtherCatFrameCodec.FormatEsmName(prev)} → {EtherCatFrameCodec.FormatEsmName(target)}：{capability}";
            Log("Info", "ESM", message);
            return true;
        }

        public bool BootToOp(out string message)
        {
            if (ActiveFault != EtherCatFaultKind.None)
            {
                ClearFaults();
            }

            if (MasterState == EsmState.Op)
            {
                message = "总线已处于 OP 运行态。";
                return true;
            }

            EsmState[] sequence = [EsmState.PreOp, EsmState.SafeOp, EsmState.Op];
            foreach (var step in sequence)
            {
                if (MasterState < step)
                {
                    if (!RequestState(step, out _, out message))
                    {
                        return false;
                    }
                }
            }

            message = "已完成顺序上线 INIT → PRE-OP → SAFE-OP → OP，所有从站进入实时 PDO 同步态。";
            return true;
        }

        public bool EnableServos(out string message)
        {
            if (MasterState != EsmState.Op)
            {
                message = $"当前处于 {EtherCatFrameCodec.FormatEsmName(MasterState)}，RxPDO 输出未激活；须先切至 OP 态才能发送 CiA 402 使能字 (0x6040)。";
                Log("Warn", "CiA402", message);
                return false;
            }

            foreach (var s in _slaves.Where(x => x.IsServoDrive && x.IsOnline))
            {
                s.HasError = false;
                s.AlStatusCode = 0x0000;
                // Execute canonical CiA 402 enable sequence: 0x06 -> 0x07 -> 0x0F
                s.Controlword = 0x000F;
                s.Cia402State = Cia402State.OperationEnabled;
            }

            message = "X/Y 伺服已完成 CiA 402 使能序列 (0x0006→0x0007→0x000F)，进入 OperationEnabled。";
            Log("Info", "CiA402", message);
            return true;
        }

        public void DisableServos()
        {
            foreach (var s in _slaves.Where(x => x.IsServoDrive))
            {
                s.Controlword = 0x0000;
                s.Cia402State = Cia402State.SwitchOnDisabled;
                s.ActualVelocity = 0;
                s.ActualTorquePermille = 0;
            }

            Log("Info", "CiA402", "已下发 0x6040=0x0000 下使能，X/Y 伺服进入 SwitchOnDisabled。");
        }

        public void SetServoTargetPosition(int slavePositionIndex, int targetCounts)
        {
            var slave = _slaves.FirstOrDefault(s => s.PositionIndex == slavePositionIndex && s.IsServoDrive);
            if (slave is null)
            {
                return;
            }

            slave.TargetPosition = targetCounts;
            if (MasterState != EsmState.Op)
            {
                Log("Warn", "RxPDO", $"已写入主站 {slave.Name} 目标位置 {targetCounts}，但当前为 {EtherCatFrameCodec.FormatEsmName(MasterState)}（安全输出锁定，驱动器不会动作）。");
            }
        }

        public void ToggleDigitalOutputBit(int bitIndex)
        {
            if (bitIndex is < 0 or > 15)
            {
                return;
            }

            var io = _slaves[3];
            io.DigitalOutputs ^= (ushort)(1 << bitIndex);
            var isOn = (io.DigitalOutputs & (1 << bitIndex)) != 0;
            Log("Info", "RxPDO", MasterState == EsmState.Op
                ? $"EL2809 DO.{bitIndex} → {(isOn ? "ON" : "OFF")} (DO=0x{io.DigitalOutputs:X4})。"
                : $"已预置 EL2809 DO.{bitIndex}={(isOn ? "ON" : "OFF")}，当前处于 {EtherCatFrameCodec.FormatEsmName(MasterState)}，切至 OP 后生效。");
        }

        public void InjectFault(EtherCatFaultKind fault)
        {
            ActiveFault = fault;
            switch (fault)
            {
                case EtherCatFaultKind.CableBreakAtSlave2:
                    // Daisy-chain cut before Slave 2 drops BOTH Slave 2 and downstream Slave 3!
                    _slaves[2].IsOnline = false;
                    _slaves[2].HasError = true;
                    _slaves[2].AlStatusCode = 0x001A;
                    _slaves[2].ActualVelocity = 0;
                    _slaves[2].ActualTorquePermille = 0;

                    _slaves[3].IsOnline = false;
                    _slaves[3].HasError = true;
                    _slaves[3].AlStatusCode = 0x001A;

                    Log("Error", "FAULT", "注入故障：Slave 2 上游网线断开！EtherCAT 串行拓扑导致 Slave 2 与下游 Slave 3 同时失联，LRW WKC 将跌落并触发告警。");
                    break;

                case EtherCatFaultKind.SmWatchdogTimeout:
                    if (MasterState == EsmState.Op)
                    {
                        MasterState = EsmState.SafeOp;
                    }

                    foreach (var s in _slaves.Where(x => x.IsServoDrive && x.IsOnline))
                    {
                        s.State = EsmState.SafeOp;
                        s.HasError = true;
                        s.AlStatusCode = 0x001B; // SM Watchdog Timeout
                        s.Cia402State = Cia402State.Fault;
                        s.ActualVelocity = 0;
                        s.ActualTorquePermille = 0;
                    }

                    Log("Error", "FAULT", "注入故障：SyncManager 看门狗超时 (AL 0x001B)！伺服自动退回 SAFE-OP+ERR 并切断输出力矩保护机械。");
                    break;

                case EtherCatFaultKind.DcSyncLost:
                    _slaves[2].HasError = true;
                    _slaves[2].AlStatusCode = 0x002D; // No SYNC0
                    _slaves[2].DcOffsetNs = 6850;
                    Log("Error", "FAULT", "注入故障：Slave 2 分布式时钟 SYNC0 失步 (AL 0x002D)，时钟偏移超限 (+6850 ns)。");
                    break;

                case EtherCatFaultKind.None:
                    ClearFaults();
                    break;
            }
        }

        public void ClearFaults()
        {
            ActiveFault = EtherCatFaultKind.None;
            foreach (var s in _slaves)
            {
                s.IsOnline = true;
                s.HasError = false;
                s.AlStatusCode = 0x0000;
                s.State = MasterState;
                if (s.IsServoDrive && s.Cia402State == Cia402State.Fault)
                {
                    s.Cia402State = Cia402State.SwitchOnDisabled;
                    s.Controlword = 0x0080; // Fault reset pulse
                }
            }

            Log("Info", "RESET", "已清除总线故障并恢复全部 4 个从站在线状态。");
        }

        /// <summary>Executes one cyclic EtherCAT LRW frame exchange + DC drift update + CiA 402 drive step.</summary>
        public (EtherCatFrameSnapshot Frame, DcJitterSample DcSample) ExecuteCycle()
        {
            CycleCount++;
            var dt = CycleTimeUs / 1_000_000.0;

            // 1. Update CSP trajectory & CiA 402 servo kinematics
            if (MasterState == EsmState.Op)
            {
                var phase = CycleCount * 0.14;
                if (CspSineTrajectoryEnabled)
                {
                    if (_slaves[1].IsOnline && _slaves[1].Cia402State == Cia402State.OperationEnabled)
                    {
                        _slaves[1].TargetPosition = (int)Math.Round(10000 * Math.Sin(phase));
                    }

                    if (_slaves[2].IsOnline && _slaves[2].Cia402State == Cia402State.OperationEnabled)
                    {
                        _slaves[2].TargetPosition = (int)Math.Round(10000 * Math.Cos(phase));
                    }
                }

                // IO Sensor loopback simulation: DI bit 3 mirrors DO bit 0 (e.g. cylinder reed switch feedback)
                if (_slaves[3].IsOnline)
                {
                    var do0 = (_slaves[3].DigitalOutputs & 0x0001) != 0;
                    _slaves[3].DigitalInputs = (ushort)(0x0005 | (do0 ? 0x0008 : 0x0000));
                }
            }

            foreach (var s in _slaves.Where(x => x.IsServoDrive))
            {
                if (!s.IsOnline || MasterState != EsmState.Op || s.Cia402State != Cia402State.OperationEnabled)
                {
                    s.ActualVelocity = 0;
                    s.ActualTorquePermille = 0;
                    continue;
                }

                var error = s.TargetPosition - s.ActualPosition;
                var maxStep = Math.Max(400, (int)(s.ProfileVelocity * Math.Max(dt, 0.02)));
                var step = Math.Clamp(error, -maxStep, maxStep);
                s.ActualPosition += step;
                s.ActualVelocity = (int)(step / Math.Max(dt, 0.001));
                s.ActualTorquePermille = (short)Math.Clamp(Math.Abs(step) * 0.18 + 65, 0, 950);
            }

            // 2. Update Distributed Clocks (DC) offsets for each slave relative to Reference Clock (Slave 0)
            _slaves[0].DcOffsetNs = 0;
            UpdateSlaveDcOffset(_slaves[1], 1);
            UpdateSlaveDcOffset(_slaves[2], 2);
            UpdateSlaveDcOffset(_slaves[3], 3);

            var maxAbsDc = _slaves.Where(s => s.IsOnline && !s.IsReferenceClock)
                .Select(s => Math.Abs(s.DcOffsetNs))
                .DefaultIfEmpty(0)
                .Max();

            var dcSample = new DcJitterSample(
                CycleCount,
                _slaves[1].IsOnline ? _slaves[1].DcOffsetNs : 0,
                _slaves[2].IsOnline ? _slaves[2].DcOffsetNs : 0,
                _slaves[3].IsOnline ? _slaves[3].DcOffsetNs : 0,
                maxAbsDc,
                DcEnabled);

            // 3. Build FMMU logical process image & calculate Expected vs Actual Working Counter (WKC)
            var image = new byte[ProcessImageLength];
            ushort expectedWkc = 0;
            ushort actualWkc = 0;

            foreach (var s in _slaves)
            {
                var exp = ExpectedWkcForSlave(s);
                var act = ActualWkcForSlave(s);
                expectedWkc += exp;
                actualWkc += act;

                if (s.IsServoDrive && s.IsOnline && MasterState >= EsmState.SafeOp)
                {
                    var sw = EtherCatFrameCodec.BuildCia402Statusword(s.Cia402State);
                    BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(s.TxPdoOffset, 2), sw);
                    BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(s.TxPdoOffset + 2, 4), s.ActualPosition);
                    BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(s.TxPdoOffset + 6, 2), s.ActualTorquePermille);
                }
                else if (s.IsIoModule && s.IsOnline && MasterState >= EsmState.SafeOp)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(s.TxPdoOffset, 2),
                        MasterState == EsmState.Op ? (ushort)(s.DigitalInputs | (s.DigitalOutputs << 8)) : s.DigitalInputs);
                }
            }

            if (actualWkc != expectedWkc)
            {
                WkcErrorCount++;
            }

            var rawFrame = EtherCatFrameCodec.BuildLrwEthernetFrame(_datagramIndex++, LogicalBaseAddress, image, actualWkc);
            var segments = EtherCatFrameCodec.DissectEthernetFrame(rawFrame, expectedWkc);
            var onlineSlaves = _slaves.Count(s => s.IsOnline);
            // Wire serialization (~4.5 us at 100Mbps) + ~0.85 us ESC processing-on-the-fly per online slave
            var frameLatencyUs = 4.6 + onlineSlaves * 0.85 + (SimulateOsJitter ? 14.5 : 0.4);

            var summary = actualWkc == expectedWkc
                ? $"周期 #{CycleCount} · LRW 正常 (WKC {actualWkc}/{expectedWkc}) · 帧往返 {frameLatencyUs:F1} µs"
                : $"周期 #{CycleCount} · WKC 异常 ({actualWkc}/{expectedWkc}) · 检测到从站离线或状态降级！";

            var frame = new EtherCatFrameSnapshot(
                CycleCount, DateTime.Now, CycleTimeUs, rawFrame, LogicalBaseAddress, ProcessImageLength,
                expectedWkc, actualWkc, frameLatencyUs, summary, segments);

            return (frame, dcSample);
        }

        public IReadOnlyList<SdoEntry> GetSdoDictionary(int slavePositionIndex)
        {
            var s = _slaves.FirstOrDefault(x => x.PositionIndex == slavePositionIndex && x.IsServoDrive) ?? _slaves[1];
            var sw = EtherCatFrameCodec.BuildCia402Statusword(s.Cia402State);
            return
            [
                new SdoEntry(0x1000, 0x00, "Device Type", "UINT32", "RO", "0x00020192", string.Empty, "CiA 402 Servo Drive Profile"),
                new SdoEntry(0x1008, 0x00, "Device Name", "STRING", "RO", s.Model, string.Empty, "厂商设备名称"),
                new SdoEntry(0x1018, 0x01, "Vendor ID", "UINT32", "RO", "0x00100000", string.Empty, "ETG 厂商标识"),
                new SdoEntry(0x1C12, 0x01, "RxPDO Assign", "UINT16", "RO", "0x1600", string.Empty, "SM2 同步管理器输出映射"),
                new SdoEntry(0x1C13, 0x01, "TxPDO Assign", "UINT16", "RO", "0x1A00", string.Empty, "SM3 同步管理器输入映射"),
                new SdoEntry(0x1C32, 0x02, "SM Output Cycle Time", "UINT32", "RO", (CycleTimeUs * 1000).ToString(CultureInfo.InvariantCulture), "ns", "DC SYNC0 同步周期"),
                new SdoEntry(0x6040, 0x00, "Controlword", "UINT16", "RW", $"0x{s.Controlword:X4}", string.Empty, "CiA 402 控制字 (0x06/0x07/0x0F)"),
                new SdoEntry(0x6041, 0x00, "Statusword", "UINT16", "RO", $"0x{sw:X4}", string.Empty, $"CiA 402 状态字 ({s.Cia402State})"),
                new SdoEntry(0x6060, 0x00, "Modes of Operation", "INT8", "RW", s.ModeOfOperation.ToString(CultureInfo.InvariantCulture), string.Empty, "1=PP, 8=CSP, 9=CSV"),
                new SdoEntry(0x6064, 0x00, "Position Actual Value", "INT32", "RO", s.ActualPosition.ToString(CultureInfo.InvariantCulture), "cnt", "当前编码器反馈位置"),
                new SdoEntry(0x6065, 0x00, "Following Error Window", "UINT32", "RW", s.FollowingErrorWindow.ToString(CultureInfo.InvariantCulture), "cnt", "跟随误差保护阈值 (100~100000)"),
                new SdoEntry(0x607A, 0x00, "Target Position", "INT32", "RW", s.TargetPosition.ToString(CultureInfo.InvariantCulture), "cnt", "目标指令位置"),
                new SdoEntry(0x6081, 0x00, "Profile Velocity", "UINT32", "RW", s.ProfileVelocity.ToString(CultureInfo.InvariantCulture), "cnt/s", "规划最大速度 (100~50000)"),
            ];
        }

        public SdoTransactionResult ReadSdo(int slavePositionIndex, ushort index, byte subIndex)
        {
            var slave = _slaves.FirstOrDefault(x => x.PositionIndex == slavePositionIndex && x.IsServoDrive);
            if (slave is null || !slave.IsOnline)
            {
                return AbortSdo(0x08000000, $"SDO Upload 0x{index:X4}:{subIndex:X2} 失败：目标从站离线。");
            }

            if (slave.State == EsmState.Init)
            {
                return AbortSdo(0x08000022, $"SDO Upload 0x{index:X4}:{subIndex:X2} 拒绝：从站处于 INIT 态，CoE 邮箱尚未启用 (Abort 0x08000022)。");
            }

            var entry = GetSdoDictionary(slavePositionIndex).FirstOrDefault(e => e.Index == index && e.SubIndex == subIndex);
            if (entry is null)
            {
                return AbortSdo(0x06020000, $"SDO Upload 0x{index:X4}:{subIndex:X2} 失败：对象字典中不存在该索引 (Abort 0x06020000)。");
            }

            var msg = $"SDO Upload [{slave.Name}] 0x{index:X4}:{subIndex:X2} ({entry.Name}) = {entry.ValueText} {entry.Unit}".Trim();
            Log("Info", "SDO-RD", msg);
            return new SdoTransactionResult(true, 0, "OK", entry.ValueText, msg);
        }

        public SdoTransactionResult WriteSdo(int slavePositionIndex, ushort index, byte subIndex, string newValueText)
        {
            var slave = _slaves.FirstOrDefault(x => x.PositionIndex == slavePositionIndex && x.IsServoDrive);
            if (slave is null || !slave.IsOnline)
            {
                return AbortSdo(0x08000000, $"SDO Download 0x{index:X4}:{subIndex:X2} 失败：目标从站离线。");
            }

            if (slave.State == EsmState.Init)
            {
                return AbortSdo(0x08000022, $"SDO Download 0x{index:X4}:{subIndex:X2} 拒绝：从站处于 INIT 态，CoE 邮箱尚未启用 (Abort 0x08000022)。");
            }

            var entry = GetSdoDictionary(slavePositionIndex).FirstOrDefault(e => e.Index == index && e.SubIndex == subIndex);
            if (entry is null)
            {
                return AbortSdo(0x06020000, $"SDO Download 0x{index:X4}:{subIndex:X2} 失败：对象字典中不存在该索引 (Abort 0x06020000)。");
            }

            if (!entry.IsWritable)
            {
                return AbortSdo(0x06010002, $"SDO Download 0x{index:X4}:{subIndex:X2} ({entry.Name}) 拒绝：只读对象禁止写入 (Abort 0x06010002)。");
            }

            var raw = (newValueText ?? string.Empty).Trim();
            switch (index)
            {
                case 0x6040:
                    if (!TryParseUInt16(raw, out var cw))
                    {
                        return AbortSdo(0x06090030, "0x6040 Controlword 格式无效（支持十进制或 0x000F 十六进制）。");
                    }

                    slave.Controlword = cw;
                    slave.Cia402State = cw switch
                    {
                        0x0006 => Cia402State.ReadyToSwitchOn,
                        0x0007 => Cia402State.SwitchedOn,
                        0x000F => Cia402State.OperationEnabled,
                        0x0080 => Cia402State.SwitchOnDisabled,
                        _ => Cia402State.SwitchOnDisabled,
                    };
                    break;

                case 0x6060:
                    if (!sbyte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode) || mode is not (1 or 8 or 9))
                    {
                        return AbortSdo(0x06090030, "0x6060 Modes of Operation 仅支持 1 (PP)、8 (CSP)、9 (CSV) (Abort 0x06090030)。");
                    }

                    slave.ModeOfOperation = mode;
                    break;

                case 0x6065:
                    if (!uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var few) || few is < 100 or > 100_000)
                    {
                        return AbortSdo(0x06090030, "0x6065 Following Error Window 范围须为 100 ~ 100000 (Abort 0x06090030)。");
                    }

                    slave.FollowingErrorWindow = few;
                    break;

                case 0x607A:
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetPos))
                    {
                        return AbortSdo(0x06090030, "0x607A Target Position 须为有效 32 位整数 (Abort 0x06090030)。");
                    }

                    slave.TargetPosition = targetPos;
                    break;

                case 0x6081:
                    if (!uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vel) || vel is < 100 or > 50_000)
                    {
                        return AbortSdo(0x06090030, "0x6081 Profile Velocity 范围须为 100 ~ 50000 (Abort 0x06090030)。");
                    }

                    slave.ProfileVelocity = vel;
                    break;
            }

            var okMsg = $"SDO Download [{slave.Name}] 0x{index:X4}:{subIndex:X2} ({entry.Name}) ← {raw} 写入成功。";
            Log("Info", "SDO-WR", okMsg);
            return new SdoTransactionResult(true, 0, "OK", raw, okMsg);
        }

        private SdoTransactionResult AbortSdo(uint code, string summary)
        {
            Log("Error", "SDO-ABT", summary);
            return new SdoTransactionResult(false, code, EtherCatFrameCodec.FormatSdoAbortCode(code), string.Empty, summary);
        }

        private void UpdateSlaveDcOffset(SlaveNode s, int hopIndex)
        {
            if (!s.IsOnline)
            {
                s.DcOffsetNs = 0;
                return;
            }

            if (ActiveFault == EtherCatFaultKind.DcSyncLost && s.PositionIndex == 2)
            {
                s.DcOffsetNs = 6500 + Math.Sin(CycleCount * 0.5) * 850 + (_rng.NextDouble() - 0.5) * 400;
                return;
            }

            if (DcEnabled)
            {
                // Hardware PI drift compensation keeps slave-to-slave SYNC0 jitter within +/- 18..65 ns
                var baseNoise = Math.Sin(CycleCount * (0.37 + hopIndex * 0.11)) * (16 + hopIndex * 6)
                                + (_rng.NextDouble() - 0.5) * 26;
                var osImpact = SimulateOsJitter ? (_rng.NextDouble() - 0.5) * 18 : 0; // Hardware DC is largely immune to OS jitter
                s.DcOffsetNs = Math.Round(baseNoise + osImpact, 1);
            }
            else
            {
                // Without DC sync, software cycle scheduling + free-running quartz drift yields multi-microsecond skew
                var drift = Math.Sin(CycleCount * (0.19 + hopIndex * 0.08)) * (7500 + hopIndex * 2800)
                            + (_rng.NextDouble() - 0.5) * 6500;
                var osSpike = SimulateOsJitter ? (_rng.NextDouble() - 0.5) * 18000 : 0;
                s.DcOffsetNs = Math.Round(drift + osSpike, 1);
            }
        }

        private ushort ExpectedWkcForSlave(SlaveNode s)
        {
            if (s.RxPdoBytes == 0 && s.TxPdoBytes == 0)
            {
                return 0;
            }

            return MasterState switch
            {
                EsmState.SafeOp => 1, // TxPDO read only (+1)
                EsmState.Op => 3,     // LRW: RxPDO write (+2) + TxPDO read (+1) = +3
                _ => 0,
            };
        }

        private ushort ActualWkcForSlave(SlaveNode s)
        {
            if (!s.IsOnline || (s.RxPdoBytes == 0 && s.TxPdoBytes == 0))
            {
                return 0;
            }

            return s.State switch
            {
                EsmState.SafeOp => 1,
                EsmState.Op => 3,
                _ => 0,
            };
        }

        private EtherCatSlaveSnapshot BuildSnapshot(SlaveNode s) => new(
            s.PositionIndex, s.StationAddress, s.Name, s.Model, s.Role,
            s.IsServoDrive, s.IsIoModule, s.IsReferenceClock, s.IsOnline,
            s.State, s.HasError, s.AlStatusCode, EtherCatFrameCodec.FormatAlStatusCode(s.AlStatusCode),
            s.PropagationDelayNs, s.DcOffsetNs,
            s.RxPdoOffset, s.RxPdoBytes, s.TxPdoOffset, s.TxPdoBytes,
            ExpectedWkcForSlave(s), ActualWkcForSlave(s),
            s.Cia402State, s.Controlword, EtherCatFrameCodec.BuildCia402Statusword(s.Cia402State),
            s.ModeOfOperation, s.TargetPosition, s.ActualPosition, s.ActualVelocity, s.ActualTorquePermille,
            s.DigitalInputs, s.DigitalOutputs);

        private static bool TryParseUInt16(string text, out ushort value)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return ushort.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }

            return ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private void Log(string level, string tag, string message) =>
            Logged?.Invoke(new EtherCatLogEntry(DateTime.Now, level, tag, message));
    }
}
