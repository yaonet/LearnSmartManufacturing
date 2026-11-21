using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.EtherCat;
using LearnSmartManufacturing.Services.EtherCat;

namespace LearnSmartManufacturing.ViewModels
{
    public sealed record EtherCatCycleOption(int CycleTimeUs, string Name);

    public sealed record EtherCatSlaveOption(int PositionIndex, string Name);

    /// <summary>
    /// Industrial EtherCAT Master Workbench ViewModel:
    /// - Tab 1: Ring Topology &amp; 0x88A4 LRW Frame Dissector, ETG.1000 ESM State Machine, CiA 402 PDO Control &amp; Fault Injection
    /// - Tab 2: CoE SDO Object Dictionary Mailbox Lab &amp; Distributed Clocks (DC) SYNC0 Jitter Oscilloscope
    /// </summary>
    public partial class EtherCatViewModel : ObservableObject, IDisposable
    {
        private const int MaxDcSamples = 64;
        private const int MaxLogs = 120;

        private readonly SimulatedEtherCatMaster _master = new();
        private readonly Queue<DcJitterSample> _dcWindow = new(MaxDcSamples);
        private readonly bool _isInitialized;

        private CancellationTokenSource? _cyclicCts;
        private bool _suppressSdoSelectionSync;
        private bool _disposed;

        public EtherCatViewModel()
        {
            CycleOptions =
            [
                new EtherCatCycleOption(250, "250 µs (4 kHz)"),
                new EtherCatCycleOption(500, "500 µs (2 kHz)"),
                new EtherCatCycleOption(1000, "1000 µs (1 kHz)"),
                new EtherCatCycleOption(2000, "2000 µs (500 Hz)"),
            ];

            ServoSlaveOptions =
            [
                new EtherCatSlaveOption(1, "Slave 1 · IS620N-X (0x1002)"),
                new EtherCatSlaveOption(2, "Slave 2 · IS620N-Y (0x1003)"),
            ];

            _master.Logged += OnMasterLogged;

            // Initialize selections after option lists are ready
            SelectedCycleOption = CycleOptions[2];
            SelectedSdoSlave = ServoSlaveOptions[0];

            // Boot ring synchronously into OP with servos enabled and pre-populate initial DC & PDO snapshots
            _master.BootToOp(out _);
            _master.EnableServos(out _);
            for (var i = 0; i < 24; i++)
            {
                ExecuteSingleCycleInternal();
            }

            RefreshSdoDictionary();
            SelectedSdoEntry = SdoEntries.FirstOrDefault(e => e.Index == 0x6065);
            StatusBanner = "EtherCAT 主站已顺序上线至 OP（4 从站在线，WKC 9/9，DC 分布式时钟已锁相）。点击“启动周期交换”查看实时动态波形。";
            _isInitialized = true;
        }

        public IReadOnlyList<EtherCatCycleOption> CycleOptions { get; }
        public IReadOnlyList<EtherCatSlaveOption> ServoSlaveOptions { get; }
        public ObservableCollection<EtherCatLogEntry> DiagnosticLogs { get; } = [];

        [ObservableProperty]
        private EtherCatCycleOption selectedCycleOption = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CyclicButtonText))]
        private bool isCyclicRunning;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MasterStateText))]
        [NotifyPropertyChangedFor(nameof(MasterStateBadgeText))]
        [NotifyPropertyChangedFor(nameof(IsMailboxAllowed))]
        [NotifyPropertyChangedFor(nameof(IsInputsActive))]
        [NotifyPropertyChangedFor(nameof(IsOutputsActive))]
        private EsmState masterState = EsmState.Init;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasActiveFault))]
        [NotifyPropertyChangedFor(nameof(ActiveFaultBadgeText))]
        private EtherCatFaultKind activeFault = EtherCatFaultKind.None;

        [ObservableProperty]
        private bool dcEnabled = true;

        [ObservableProperty]
        private bool simulateOsJitter;

        [ObservableProperty]
        private bool cspSineTrajectoryEnabled = true;

        [ObservableProperty]
        private string manualTargetXText = "5000";

        [ObservableProperty]
        private string manualTargetYText = "-3000";

        [ObservableProperty]
        private bool doBit0;

        [ObservableProperty]
        private bool doBit1;

        [ObservableProperty]
        private bool doBit2;

        [ObservableProperty]
        private bool doBit3;

        [ObservableProperty]
        private IReadOnlyList<EtherCatSlaveSnapshot> slaveSnapshots = [];

        [ObservableProperty]
        private EtherCatFrameSnapshot? latestFrame;

        [ObservableProperty]
        private IReadOnlyList<DcJitterSample> dcJitterSamples = [];

        [ObservableProperty]
        private double framePulsePhase;

        [ObservableProperty]
        private long cycleCount;

        [ObservableProperty]
        private long wkcErrorCount;

        [ObservableProperty]
        private string wkcSummaryText = "0 / 0";

        [ObservableProperty]
        private bool isWkcValid = true;

        [ObservableProperty]
        private string frameLatencyText = "0.0 µs";

        [ObservableProperty]
        private string maxDcOffsetText = "0.0 ns";

        [ObservableProperty]
        private bool isDcWithinSpec = true;

        [ObservableProperty]
        private string statusBanner = string.Empty;

        // Tab 2: CoE SDO Object Dictionary Mailbox Lab
        [ObservableProperty]
        private EtherCatSlaveOption selectedSdoSlave = null!;

        [ObservableProperty]
        private IReadOnlyList<SdoEntry> sdoEntries = [];

        [ObservableProperty]
        private SdoEntry? selectedSdoEntry;

        [ObservableProperty]
        private string sdoIndexText = "0x6065";

        [ObservableProperty]
        private string sdoSubIndexText = "0x00";

        [ObservableProperty]
        private string sdoWriteValueText = "5000";

        [ObservableProperty]
        private string lastSdoBanner = "选中左侧对象字典条目后，可测试 SDO Upload（读取）或 Download（写入）；切换到 INIT 态将触发 Abort 0x08000022。";

        [ObservableProperty]
        private bool isLastSdoSuccess = true;

        public string CyclicButtonText => IsCyclicRunning ? "停止周期交换" : "启动周期交换";
        public string MasterStateText => EtherCatFrameCodec.FormatEsmName(MasterState);
        public string MasterStateBadgeText => HasActiveFault ? $"{MasterStateText} · FAULT" : $"ESM · {MasterStateText}";
        public bool IsMailboxAllowed => MasterState is EsmState.PreOp or EsmState.SafeOp or EsmState.Op;
        public bool IsInputsActive => MasterState is EsmState.SafeOp or EsmState.Op;
        public bool IsOutputsActive => MasterState == EsmState.Op;
        public bool HasActiveFault => ActiveFault != EtherCatFaultKind.None;

        public string ActiveFaultBadgeText => ActiveFault switch
        {
            EtherCatFaultKind.CableBreakAtSlave2 => "故障：Slave 2 断链 (WKC 跌落)",
            EtherCatFaultKind.SmWatchdogTimeout => "故障：SM 看门狗超时 (AL 0x001B)",
            EtherCatFaultKind.DcSyncLost => "故障：Slave 2 DC 失步 (AL 0x002D)",
            _ => "总线健康 · 无活动故障",
        };

        partial void OnSelectedCycleOptionChanged(EtherCatCycleOption value)
        {
            if (value is null)
            {
                return;
            }

            _master.CycleTimeUs = value.CycleTimeUs;
            if (_isInitialized)
            {
                RefreshSdoDictionary();
                StatusBanner = $"已切换 EtherCAT 同步周期为 {value.Name}（同步更新对象字典 0x1C32:02 = {value.CycleTimeUs * 1000} ns）。";
            }
        }

        partial void OnDcEnabledChanged(bool value)
        {
            _master.DcEnabled = value;
            if (!_isInitialized)
            {
                return;
            }

            // Pump a few cycles immediately when paused so the user visually sees the oscilloscope scale jump
            if (!IsCyclicRunning)
            {
                for (var i = 0; i < 6; i++)
                {
                    ExecuteSingleCycleInternal();
                }
            }

            StatusBanner = value
                ? "已开启分布式时钟 (DC) PI 漂移补偿：各从站 SYNC0 偏差锁定在 ±15~65 ns 纳秒级。"
                : "已关闭分布式时钟 (DC)：失去硬件锁相，从站自由晶振漂移与调度抖动扩大至微秒级 (±10~25 µs)。";
        }

        partial void OnSimulateOsJitterChanged(bool value)
        {
            _master.SimulateOsJitter = value;
            if (_isInitialized && !IsCyclicRunning)
            {
                for (var i = 0; i < 4; i++)
                {
                    ExecuteSingleCycleInternal();
                }
            }
        }

        partial void OnCspSineTrajectoryEnabledChanged(bool value)
        {
            _master.CspSineTrajectoryEnabled = value;
        }

        partial void OnSelectedSdoSlaveChanged(EtherCatSlaveOption value)
        {
            if (!_isInitialized || value is null)
            {
                return;
            }

            RefreshSdoDictionary();
        }

        partial void OnSelectedSdoEntryChanged(SdoEntry? value)
        {
            if (value is null || _suppressSdoSelectionSync)
            {
                return;
            }

            SdoIndexText = $"0x{value.Index:X4}";
            SdoSubIndexText = $"0x{value.SubIndex:X2}";
            SdoWriteValueText = value.ValueText;
        }

        [RelayCommand]
        private void ToggleCyclic()
        {
            if (IsCyclicRunning)
            {
                StopCyclic();
                StatusBanner = "已暂停周期性 LRW 过程数据交换。";
                return;
            }

            IsCyclicRunning = true;
            _cyclicCts = new CancellationTokenSource();
            var ct = _cyclicCts.Token;
            StatusBanner = $"已启动 EtherCAT 实时周期交换（同步周期 {SelectedCycleOption.Name}）。";
            _ = RunCyclicLoopAsync(ct);
        }

        [RelayCommand]
        private void StepCycle()
        {
            ExecuteSingleCycleInternal();
            RefreshSdoDictionary();
            StatusBanner = LatestFrame?.FrameSummary ?? "已执行单步 LRW 周期。";
        }

        [RelayCommand]
        private void BootToOp()
        {
            _master.BootToOp(out var msg);
            _master.EnableServos(out _);
            ExecuteSingleCycleInternal();
            RefreshSdoDictionary();
            StatusBanner = msg;
        }

        [RelayCommand]
        private void RequestEsmState(string? stateName)
        {
            if (!Enum.TryParse<EsmState>(stateName, ignoreCase: true, out var target))
            {
                return;
            }

            var ok = _master.RequestState(target, out _, out var msg);
            ExecuteSingleCycleInternal();
            RefreshSdoDictionary();
            StatusBanner = ok ? msg : $"[拒绝] {msg}";
        }

        [RelayCommand]
        private void EnableServos()
        {
            _master.EnableServos(out var msg);
            ExecuteSingleCycleInternal();
            RefreshSdoDictionary();
            StatusBanner = msg;
        }

        [RelayCommand]
        private void DisableServos()
        {
            _master.DisableServos();
            ExecuteSingleCycleInternal();
            RefreshSdoDictionary();
            StatusBanner = "已下发 CiA 402 下使能指令 (0x6040=0x0000)。";
        }

        [RelayCommand]
        private void ApplyManualTargets()
        {
            if (!int.TryParse(ManualTargetXText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tx) ||
                !int.TryParse(ManualTargetYText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ty))
            {
                StatusBanner = "目标位置格式无效，请输入有效整数编码器脉冲值。";
                return;
            }

            CspSineTrajectoryEnabled = false;
            _master.SetServoTargetPosition(1, tx);
            _master.SetServoTargetPosition(2, ty);
            if (!IsCyclicRunning)
            {
                for (var i = 0; i < 4; i++)
                {
                    ExecuteSingleCycleInternal();
                }
            }

            RefreshSdoDictionary();
            StatusBanner = MasterState == EsmState.Op
                ? $"已通过 RxPDO (0x607A) 下发手动目标位置：X={tx}, Y={ty}。"
                : $"已写入主站缓冲 X={tx}, Y={ty}，但当前为 {MasterStateText}（RxPDO 输出已锁定在安全态，伺服保持静止）。";
        }

        [RelayCommand]
        private void ToggleDoBit(string? bitStr)
        {
            if (!int.TryParse(bitStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bit))
            {
                return;
            }

            _master.ToggleDigitalOutputBit(bit);
            if (!IsCyclicRunning)
            {
                ExecuteSingleCycleInternal();
            }
        }

        [RelayCommand]
        private void InjectFault(string? faultName)
        {
            if (!Enum.TryParse<EtherCatFaultKind>(faultName, ignoreCase: true, out var fault))
            {
                return;
            }

            _master.InjectFault(fault);
            ExecuteSingleCycleInternal();
            RefreshSdoDictionary();
            StatusBanner = ActiveFaultBadgeText;
        }

        [RelayCommand]
        private void ClearFaults()
        {
            _master.ClearFaults();
            ExecuteSingleCycleInternal();
            RefreshSdoDictionary();
            StatusBanner = "已复位所有总线故障标志，可点击“一键上线 (Boot→OP)”恢复全轴同步。";
        }

        [RelayCommand]
        private void ReadSdo()
        {
            if (!TryParseSdoAddress(out var index, out var subIndex, out var err))
            {
                IsLastSdoSuccess = false;
                LastSdoBanner = err;
                return;
            }

            var result = _master.ReadSdo(SelectedSdoSlave.PositionIndex, index, subIndex);
            IsLastSdoSuccess = result.Success;
            LastSdoBanner = result.Success
                ? $"✓ {result.Summary}"
                : $"✕ {result.Summary} [{result.AbortCodeHex}: {result.AbortText}]";
            if (result.Success)
            {
                SdoWriteValueText = result.ValueText;
            }
        }

        [RelayCommand]
        private void WriteSdo()
        {
            if (!TryParseSdoAddress(out var index, out var subIndex, out var err))
            {
                IsLastSdoSuccess = false;
                LastSdoBanner = err;
                return;
            }

            var result = _master.WriteSdo(SelectedSdoSlave.PositionIndex, index, subIndex, SdoWriteValueText);
            IsLastSdoSuccess = result.Success;
            LastSdoBanner = result.Success
                ? $"✓ {result.Summary}"
                : $"✕ {result.Summary} [{result.AbortCodeHex}: {result.AbortText}]";

            if (result.Success)
            {
                ExecuteSingleCycleInternal();
                RefreshSdoDictionary();
            }
        }

        [RelayCommand]
        private void DemoSdoAbort(string? scenario)
        {
            SdoTransactionResult result;
            switch (scenario)
            {
                case "ReadOnly":
                    SdoIndexText = "0x1000";
                    SdoSubIndexText = "0x00";
                    SdoWriteValueText = "999";
                    result = _master.WriteSdo(SelectedSdoSlave.PositionIndex, 0x1000, 0x00, "999");
                    break;

                case "NotFound":
                    SdoIndexText = "0x9999";
                    SdoSubIndexText = "0x00";
                    result = _master.ReadSdo(SelectedSdoSlave.PositionIndex, 0x9999, 0x00);
                    break;

                case "OutOfRange":
                    SdoIndexText = "0x6060";
                    SdoSubIndexText = "0x00";
                    SdoWriteValueText = "99";
                    result = _master.WriteSdo(SelectedSdoSlave.PositionIndex, 0x6060, 0x00, "99");
                    break;

                default:
                    return;
            }

            IsLastSdoSuccess = result.Success;
            LastSdoBanner = $"✕ {result.Summary} [{result.AbortCodeHex}: {result.AbortText}]";
        }

        [RelayCommand]
        private void ClearLogs()
        {
            DiagnosticLogs.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopCyclic();
        }

        private async Task RunCyclicLoopAsync(CancellationToken ct)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(55));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    ExecuteSingleCycleInternal();
                    if (CycleCount % 5 == 0)
                    {
                        RefreshSdoDictionary();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped normally.
            }
            finally
            {
                IsCyclicRunning = false;
            }
        }

        private void StopCyclic()
        {
            if (_cyclicCts is not null)
            {
                _cyclicCts.Cancel();
                _cyclicCts.Dispose();
                _cyclicCts = null;
            }

            IsCyclicRunning = false;
        }

        private void ExecuteSingleCycleInternal()
        {
            var (frame, dcSample) = _master.ExecuteCycle();
            if (_dcWindow.Count >= MaxDcSamples)
            {
                _dcWindow.Dequeue();
            }

            _dcWindow.Enqueue(dcSample);

            var snaps = _master.GetSlaveSnapshots();
            SlaveSnapshots = snaps;
            LatestFrame = frame;
            DcJitterSamples = [.. _dcWindow];
            FramePulsePhase = (FramePulsePhase + 0.14) % 1.0;

            MasterState = _master.MasterState;
            ActiveFault = _master.ActiveFault;
            CycleCount = _master.CycleCount;
            WkcErrorCount = _master.WkcErrorCount;
            WkcSummaryText = frame.WkcText;
            IsWkcValid = frame.IsWkcValid;
            FrameLatencyText = $"{frame.FrameLatencyUs:F1} µs";
            MaxDcOffsetText = dcSample.MaxAbsOffsetNs >= 1000
                ? $"±{dcSample.MaxAbsOffsetNs / 1000.0:F2} µs"
                : $"±{dcSample.MaxAbsOffsetNs:F1} ns";
            IsDcWithinSpec = dcSample.MaxAbsOffsetNs <= 1000;

            var io = snaps[3];
            DoBit0 = (io.DigitalOutputs & 0x0001) != 0;
            DoBit1 = (io.DigitalOutputs & 0x0002) != 0;
            DoBit2 = (io.DigitalOutputs & 0x0004) != 0;
            DoBit3 = (io.DigitalOutputs & 0x0008) != 0;
        }

        private void RefreshSdoDictionary()
        {
            var slaveIndex = SelectedSdoSlave?.PositionIndex ?? 1;
            var prevIndex = SelectedSdoEntry?.Index;
            var prevSub = SelectedSdoEntry?.SubIndex;

            var list = _master.GetSdoDictionary(slaveIndex);
            SdoEntries = list;
            if (prevIndex is not null && prevSub is not null)
            {
                var found = list.FirstOrDefault(e => e.Index == prevIndex && e.SubIndex == prevSub);
                if (found is not null)
                {
                    _suppressSdoSelectionSync = true;
                    try
                    {
                        SelectedSdoEntry = found;
                    }
                    finally
                    {
                        _suppressSdoSelectionSync = false;
                    }
                }
            }
        }

        private bool TryParseSdoAddress(out ushort index, out byte subIndex, out string error)
        {
            index = 0;
            subIndex = 0;
            error = string.Empty;

            if (!TryParseHexOrDecU16(SdoIndexText, out index))
            {
                error = "SDO 索引格式无效（例如 0x6065）。";
                return false;
            }

            if (!TryParseHexOrDecU8(SdoSubIndexText, out subIndex))
            {
                error = "SDO 子索引格式无效（例如 0x00）。";
                return false;
            }

            return true;
        }

        private static bool TryParseHexOrDecU16(string? text, out ushort value)
        {
            var s = (text ?? string.Empty).Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return ushort.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }

            return ushort.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseHexOrDecU8(string? text, out byte value)
        {
            var s = (text ?? string.Empty).Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return byte.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }

            return byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private void OnMasterLogged(EtherCatLogEntry entry)
        {
            DiagnosticLogs.Insert(0, entry);
            while (DiagnosticLogs.Count > MaxLogs)
            {
                DiagnosticLogs.RemoveAt(DiagnosticLogs.Count - 1);
            }
        }
    }
}
