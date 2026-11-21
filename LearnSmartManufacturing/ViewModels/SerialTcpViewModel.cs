using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.SerialTcp;
using LearnSmartManufacturing.Services.SerialTcp;

namespace LearnSmartManufacturing.ViewModels
{
    public sealed record FramingProtocolOption(FramingProtocolKind Kind, string Name, string SpecSummary);

    public sealed record LinkTransportOption(LinkTransportKind Kind, string Name);

    public sealed record BaudRateOption(int BaudRate, string Name);

    public sealed record SerialFormatOption(string Name, int DataBits, bool HasParity, double StopBits);

    /// <summary>
    /// Industrial TCP / Serial Custom Protocol Framing (Sticky/Half-Packet/EMI Resync/CRC-16),
    /// RS485 Half-Duplex Bus Arbitration + Exponential-Backoff Reconnect, and Serial Baud Timing Workbench ViewModel.
    /// </summary>
    public partial class SerialTcpViewModel : ObservableObject, IDisposable
    {
        private const int MaxLogEntries = 80;

        private readonly StreamFrameDecoder _decoder = new();
        private readonly LoopbackTcpChannel _loopback = new();
        private readonly SynchronizationContext? _uiContext;
        private readonly Queue<(byte[] Bytes, string Label, string Kind)> _pendingFragments = new();
        private readonly List<StreamChunkRecord> _canvasChunks = [];
        private readonly List<DecodedFrameRecord> _canvasFrames = [];
        private readonly List<Rs485BusEvent> _canvasBusEvents = [];
        private readonly bool _isInitialized;

        private int _nextChunkId = 1;
        private byte _txSeq = 1;
        private long _busEventId = 1;
        private bool _disposed;

        public SerialTcpViewModel()
        {
            _uiContext = SynchronizationContext.Current;

            ProtocolOptions =
            [
                new(FramingProtocolKind.BinaryHeaderLengthCrc, "二进制帧 [AA 55 + 长度 + CRC-16]",
                    "格式：[0xAA 0x55][Cmd][Seq][Len][Payload][CRC16-LE] · 抗 EMI 杂波重同步与 CRC 检错"),
                new(FramingProtocolKind.AsciiDelimiterCrLf, "ASCII 分隔符帧 (\\r\\n · 扫码枪/电子秤)",
                    "格式：<ASCII 文本> + 0x0D 0x0A (\\r\\n) · 工业扫码枪/激光打标/称重仪表最常见"),
                new(FramingProtocolKind.FixedLength12Byte, "定长 12 字节帧 (单片机/测距仪)",
                    "格式：[0xFE][ID][Float1:4B][Float2:4B][Sum8][0xEF] · 固定 12B 零动态分配切片"),
            ];

            TransportOptions =
            [
                new(LinkTransportKind.SimulatedByteStream, "内存字节流仿真 (确定性单步拆包)"),
                new(LinkTransportKind.RealLoopbackTcpSocket, "真实本机 TCP Socket 环回 (127.0.0.1)"),
            ];

            BaudRateOptions =
            [
                new(9600, "9600 bps (传统表计)"),
                new(19200, "19200 bps (Modbus RTU)"),
                new(38400, "38400 bps"),
                new(57600, "57600 bps"),
                new(115200, "115200 bps (高速串口常用)"),
                new(921600, "921600 bps (高速传感器)"),
            ];

            SerialFormatOptions =
            [
                new("8N1 (10 bit/B · 无校验)", 8, false, 1.0),
                new("8E1 (11 bit/B · 偶校验)", 8, true, 1.0),
                new("8N2 (11 bit/B · 2 停止位)", 8, false, 2.0),
            ];

            _loopback.ChunkReceived += chunk => RunOnUi(() => ProcessIncomingChunk(chunk, "TCP Read()", "Normal"));

            SelectedProtocol = ProtocolOptions[0];
            SelectedTransport = TransportOptions[0];
            SelectedBaudRate = BaudRateOptions[4];     // 115200 bps
            SelectedSerialFormat = SerialFormatOptions[0]; // 8N1

            _isInitialized = true;

            // Populate clean initial snapshots synchronously so both Tab 1 & Tab 2 are rich on open
            SendSingleFrameInternal("TEMP=186.5C,OK", "初始单帧", "Normal");
            SeedInitialRs485History();
            RecalculateSerialTiming();
            StatusBanner = "TCP/串口自定义协议工作台就绪：可在 Tab 1 演练粘包/半包/杂波重同步，或在 Tab 2 演示 RS485 半双工防冲突与波特率耗时。";
        }

        public IReadOnlyList<FramingProtocolOption> ProtocolOptions { get; }
        public IReadOnlyList<LinkTransportOption> TransportOptions { get; }
        public IReadOnlyList<BaudRateOption> BaudRateOptions { get; }
        public IReadOnlyList<SerialFormatOption> SerialFormatOptions { get; }

        public ObservableCollection<DecodedFrameRecord> DecodedFrames { get; } = [];
        public ObservableCollection<StreamChunkRecord> RawChunks { get; } = [];
        public ObservableCollection<Rs485BusEvent> Rs485EventLog { get; } = [];

        #region Tab 1: Stream Framing

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ProtocolSpecSummary))]
        private FramingProtocolOption selectedProtocol = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsRealTcpMode))]
        [NotifyPropertyChangedFor(nameof(TransportStatusBadge))]
        private LinkTransportOption selectedTransport = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TransportStatusBadge))]
        [NotifyPropertyChangedFor(nameof(LoopbackToggleText))]
        private bool isLoopbackRunning;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TransportStatusBadge))]
        private int loopbackPort;

        [ObservableProperty]
        private string customPayloadText = "SN:PCB-20261121-001,OK";

        [ObservableProperty]
        private FramingPipelineSnapshot? pipelineSnapshot;

        [ObservableProperty]
        private int validFrameCount;

        [ObservableProperty]
        private int crcErrorCount;

        [ObservableProperty]
        private int garbageBytesDiscarded;

        [ObservableProperty]
        private int pendingBufferBytes;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPendingFragments))]
        [NotifyPropertyChangedFor(nameof(DeliverFragmentButtonText))]
        private int pendingFragmentCount;

        [ObservableProperty]
        private string statusBanner = string.Empty;

        public string ProtocolSpecSummary => SelectedProtocol?.SpecSummary ?? string.Empty;
        public bool IsRealTcpMode => SelectedTransport?.Kind == LinkTransportKind.RealLoopbackTcpSocket;
        public bool HasPendingFragments => PendingFragmentCount > 0;
        public string DeliverFragmentButtonText => HasPendingFragments ? $"投递下一半包碎片 (余 {PendingFragmentCount})" : "无挂起碎片";
        public string LoopbackToggleText => IsLoopbackRunning ? "关闭本机 TCP" : "启动本机 TCP";

        public string TransportStatusBadge => IsRealTcpMode
            ? (IsLoopbackRunning ? $"TCP LOOPBACK · 127.0.0.1:{LoopbackPort}" : "TCP LOOPBACK · 未启动")
            : "BYTE STREAM · 仿真闭环";

        #endregion

        #region Tab 2: RS485 Half-Duplex, Reconnect Backoff & Baud Rate Calculator

        [ObservableProperty]
        private bool halfDuplexLockEnabled = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ReconnectStatusText))]
        [NotifyPropertyChangedFor(nameof(CanStepBackoff))]
        private ReconnectLinkState linkState = ReconnectLinkState.Connected;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ReconnectStatusText))]
        private int reconnectAttempt;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ReconnectStatusText))]
        private int nextBackoffSec = 1;

        [ObservableProperty]
        private int collisionCount;

        [ObservableProperty]
        private int rs485SuccessCount;

        [ObservableProperty]
        private IReadOnlyList<Rs485BusEvent> rs485BusEvents = [];

        [ObservableProperty]
        private BaudRateOption selectedBaudRate = null!;

        [ObservableProperty]
        private SerialFormatOption selectedSerialFormat = null!;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TxBytesText))]
        private int txBytesCount = 8;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RxBytesText))]
        private int rxBytesCount = 16;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TurnaroundText))]
        private double turnaroundMs = 2.0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ByteTimeText))]
        [NotifyPropertyChangedFor(nameof(FrameTimeText))]
        [NotifyPropertyChangedFor(nameof(T35SilentText))]
        [NotifyPropertyChangedFor(nameof(MaxPollHzText))]
        private SerialTimingResult serialTiming = null!;

        public bool CanStepBackoff => LinkState is ReconnectLinkState.HeartbeatTimeout or ReconnectLinkState.BackoffReconnecting;

        public string ReconnectStatusText => LinkState switch
        {
            ReconnectLinkState.Connected => "CONNECTED · 心跳 PING/PONG 正常",
            ReconnectLinkState.HeartbeatTimeout => "HEARTBEAT TIMEOUT · 连续 3 次未应答",
            ReconnectLinkState.BackoffReconnecting => $"BACKOFF 重连中 · 第 {ReconnectAttempt} 次 (退避 {NextBackoffSec}s)",
            _ => "DISCONNECTED",
        };

        public string TxBytesText => $"{TxBytesCount} B";
        public string RxBytesText => $"{RxBytesCount} B";
        public string TurnaroundText => $"{TurnaroundMs:F1} ms";
        public string ByteTimeText => SerialTiming is null ? "--" : $"{SerialTiming.ByteTimeMs:F3} ms";
        public string FrameTimeText => SerialTiming is null ? "--" : $"{SerialTiming.FrameTimeMs:F2} ms";
        public string T35SilentText => SerialTiming is null ? "--" : $"{SerialTiming.T35SilentMs:F2} ms";
        public string MaxPollHzText => SerialTiming is null ? "--" : $"{SerialTiming.MaxPollHz:F1} Hz";

        #endregion

        partial void OnSelectedProtocolChanged(FramingProtocolOption value)
        {
            if (!_isInitialized || value is null) return;
            _decoder.SetProtocol(value.Kind);
            ClearBuffersOnly();
            CustomPayloadText = value.Kind switch
            {
                FramingProtocolKind.AsciiDelimiterCrLf => "SN:PCB-20261121-001,OK",
                FramingProtocolKind.FixedLength12Byte => "24.65, 38.2",
                _ => "TEMP=186.5C,OK",
            };
            SendSingleFrameInternal(CustomPayloadText, "切换协议单帧", "Normal");
            StatusBanner = $"已切换拆包协议至：{value.Name}。";
        }

        partial void OnSelectedTransportChanged(LinkTransportOption value)
        {
            if (!_isInitialized || value is null) return;
            if (value.Kind == LinkTransportKind.SimulatedByteStream && IsLoopbackRunning)
            {
                _loopback.Stop();
                IsLoopbackRunning = false;
                LoopbackPort = 0;
            }

            StatusBanner = value.Kind == LinkTransportKind.RealLoopbackTcpSocket
                ? "已切换至真实本机 TCP Socket 环回模式，点击“启动本机 TCP”即可通过真实操作系统 Socket 收发字节流。"
                : "已切换至内存确定性字节流模式（支持分步演示半包挂起与恢复）。";
        }

        partial void OnSelectedBaudRateChanged(BaudRateOption value) { if (_isInitialized) RecalculateSerialTiming(); }
        partial void OnSelectedSerialFormatChanged(SerialFormatOption value) { if (_isInitialized) RecalculateSerialTiming(); }
        partial void OnTxBytesCountChanged(int value) { if (_isInitialized) RecalculateSerialTiming(); }
        partial void OnRxBytesCountChanged(int value) { if (_isInitialized) RecalculateSerialTiming(); }
        partial void OnTurnaroundMsChanged(double value) { if (_isInitialized) RecalculateSerialTiming(); }

        [RelayCommand]
        private async Task ToggleLoopbackAsync()
        {
            if (IsLoopbackRunning)
            {
                _loopback.Stop();
                IsLoopbackRunning = false;
                LoopbackPort = 0;
                StatusBanner = "已关闭本机 TCP 环回 Socket。";
                return;
            }

            try
            {
                await _loopback.StartAsync();
                IsLoopbackRunning = true;
                LoopbackPort = _loopback.Port;
                StatusBanner = $"本机真实 TCP 环回已建立 (127.0.0.1:{LoopbackPort})，现在点击下方压测按钮将真实走操作系统 Socket 栈！";
            }
            catch (Exception ex)
            {
                StatusBanner = $"启动本机 TCP 环回失败：{ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SendSingleFrameAsync()
        {
            var raw = BuildFrameForCurrentProtocol(CustomPayloadText, _txSeq++);
            await DispatchBytesAsync(raw, "正常单帧", "Normal");
            StatusBanner = $"已发送 1 帧完整报文 ({raw.Length} B)，状态机一次提取成功。";
        }

        [RelayCommand]
        private async Task SendStickyBurstAsync()
        {
            var f1 = BuildFrameForCurrentProtocol("PART-01,OK", _txSeq++);
            var f2 = BuildFrameForCurrentProtocol("PART-02,OK", _txSeq++);
            var f3 = BuildFrameForCurrentProtocol("PART-03,NG", _txSeq++);
            var merged = Concat(f1, f2, f3);

            await DispatchBytesAsync(merged, "3帧粘包合并", "Sticky");
            StatusBanner = $"演示 TCP 粘包：3 个独立业务帧合并为 1 个 {merged.Length} B 字节块到达，状态机单次循环完整拆出 3 帧！";
        }

        [RelayCommand]
        private async Task StartHalfPacketDemoAsync()
        {
            _pendingFragments.Clear();
            var full = BuildFrameForCurrentProtocol(CustomPayloadText, _txSeq++);
            var cut1 = Math.Max(2, full.Length / 3);
            var cut2 = Math.Max(cut1 + 2, (full.Length * 2) / 3);

            var part1 = full[..cut1];
            var part2 = full[cut1..cut2];
            var part3 = full[cut2..];

            _pendingFragments.Enqueue((part2, "半包碎片 2/3", "Half"));
            _pendingFragments.Enqueue((part3, "半包尾片 3/3", "Half"));
            PendingFragmentCount = _pendingFragments.Count;

            await DispatchBytesAsync(part1, "半包碎片 1/3", "Half");
            StatusBanner = $"演示半包（碎片 1/3 · {part1.Length} B）：字节不足整帧，已挂起在 RxBuffer 滑动缓存区；点击“投递下一半包碎片”继续拼帧！";
        }

        [RelayCommand(CanExecute = nameof(HasPendingFragments))]
        private async Task DeliverNextFragmentAsync()
        {
            if (_pendingFragments.Count == 0) return;
            var (bytes, label, kind) = _pendingFragments.Dequeue();
            PendingFragmentCount = _pendingFragments.Count;
            await DispatchBytesAsync(bytes, label, kind);

            StatusBanner = PendingFragmentCount > 0
                ? $"已投递 {label} ({bytes.Length} B)，RxBuffer 累积 {PendingBufferBytes} B 仍等待剩余尾片。"
                : $"已投递 {label} ({bytes.Length} B)，完整帧凑齐并从 RxBuffer 消费拆出！";
        }

        [RelayCommand]
        private async Task SendChaosStressTestAsync()
        {
            // EMI garbage noise + Valid Frame A + Corrupt Frame B + Valid Frame C (split into two chunks!)
            byte[] noise = [0x13, 0xFF, 0x00, 0x7E];
            var validA = BuildFrameForCurrentProtocol("CH1=12.48mm", _txSeq++);
            var corruptB = SelectedProtocol.Kind == FramingProtocolKind.AsciiDelimiterCrLf
                ? CustomProtocolCodec.BuildAsciiFrame("CH2=RECOVERED_ASCII")
                : CustomProtocolCodec.CorruptLastByte(BuildFrameForCurrentProtocol("CORRUPT-DATA", _txSeq++));
            var validC = BuildFrameForCurrentProtocol("CH3=99.85%", _txSeq++);

            var splitIdx = Math.Max(2, validC.Length / 2);
            var chunk1 = Concat(noise, validA, corruptB, validC[..splitIdx]);
            var chunk2 = validC[splitIdx..];

            await DispatchBytesAsync(chunk1, "杂波+粘包+错帧+半包头", "Chaos");
            if (IsRealTcpMode && IsLoopbackRunning)
            {
                await Task.Delay(30);
            }

            await DispatchBytesAsync(chunk2, "半包尾部补齐", "Half");
            StatusBanner = "混合压测完成：状态机自动丢弃前导 EMI 杂波、拦截 CRC 错帧、缓存半包并成功恢复所有合法帧！";
        }

        [RelayCommand]
        private void ClearFramingData()
        {
            _decoder.Reset();
            ClearBuffersOnly();
            StatusBanner = "已清空接收缓存与拆包计数。";
        }

        [RelayCommand]
        private void PollRs485AllDevices()
        {
            if (LinkState != ReconnectLinkState.Connected)
            {
                StatusBanner = "当前链路处于断线/重连状态，请先完成重连恢复后再轮询。";
                return;
            }

            if (!HalfDuplexLockEnabled)
            {
                SimulateConcurrentConflict();
                return;
            }

            var devices = new (string Name, string Reply)[]
            {
                ("01 扫码枪", "SN:B260118"),
                ("02 激光测距", "14.285 mm"),
                ("03 温控表", "185.4 °C"),
            };

            foreach (var (name, reply) in devices)
            {
                RecordBusEvent(name, "TX", $"Master → {name} 轮询请求", SerialTiming.FrameTimeMs * 0.4, false);
                RecordBusEvent(name, "RX", $"{name} → Master: {reply}", SerialTiming.FrameTimeMs * 0.6 + TurnaroundMs, false);
                Rs485SuccessCount++;
            }

            StatusBanner = "RS485 半双工互斥锁 (SemaphoreSlim) 按序完成 3 台从站串行轮询，0 帧冲突。";
        }

        [RelayCommand]
        private void SimulateConcurrentConflict()
        {
            HalfDuplexLockEnabled = false;
            if (LinkState != ReconnectLinkState.Connected)
            {
                LinkState = ReconnectLinkState.Connected;
            }

            RecordBusEvent("01 扫码枪", "TX", "线程 A 发送读码指令", 1.2, false);
            RecordBusEvent("02 激光测距", "TX", "线程 B 未加锁并发写入！", 0.3, true);
            RecordBusEvent("A+/B- 冲突", "COLLISION", "两帧电平叠加碰撞 → 收到乱码 0xFE 0x3D 0x91 (CRC ERR)", 2.8, true);
            CollisionCount++;
            StatusBanner = "⚠ 演示 RS485 半双工冲突：未加互斥锁导致两线程同时占用 A+/B- 差分总线，通信帧直接乱码失效！";
        }

        [RelayCommand]
        private void SimulateLinkDrop()
        {
            LinkState = ReconnectLinkState.HeartbeatTimeout;
            ReconnectAttempt = 0;
            NextBackoffSec = 1;
            RecordBusEvent("链路监控", "HEARTBEAT", "连续 3 次心跳 PING 超时（拔线/掉电），触发断线保护！", 3000, true);
            StatusBanner = "已模拟链路中断（心跳超时），点击“推进退避重连”演示 1s → 2s → 4s 指数退避自动恢复。";
            StepBackoffReconnectCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanStepBackoff))]
        private void StepBackoffReconnect()
        {
            ReconnectAttempt++;
            if (ReconnectAttempt < 3)
            {
                LinkState = ReconnectLinkState.BackoffReconnecting;
                var waitSec = 1 << (ReconnectAttempt - 1); // 1s, 2s
                NextBackoffSec = 1 << ReconnectAttempt;    // next 2s, 4s
                RecordBusEvent($"重连 #{ReconnectAttempt}", "RECONNECT", $"退避 {waitSec}s 后尝试重连失败，下次退避 {NextBackoffSec}s (防止重连风暴)", waitSec * 1000, true);
                StatusBanner = $"指数退避第 {ReconnectAttempt} 次重试未通，退避间隔翻倍至 {NextBackoffSec}s，再点一次完成恢复。";
            }
            else
            {
                LinkState = ReconnectLinkState.Connected;
                RecordBusEvent($"重连 #{ReconnectAttempt}", "RX", "TCP/串口重连成功！重新注册心跳并恢复业务轮询", 42, false);
                StatusBanner = "第 3 次退避重连成功，心跳与 RS485 总线已自动恢复在线！";
            }

            StepBackoffReconnectCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void RestoreLinkAndLock()
        {
            HalfDuplexLockEnabled = true;
            LinkState = ReconnectLinkState.Connected;
            ReconnectAttempt = 0;
            NextBackoffSec = 1;
            RecordBusEvent("链路监控", "HEARTBEAT", "已恢复连接并开启 RS485 半双工互斥锁", 1.5, false);
            StatusBanner = "已恢复连接与半双工互斥锁保护。";
            StepBackoffReconnectCommand.NotifyCanExecuteChanged();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _loopback.Dispose();
        }

        private async Task DispatchBytesAsync(byte[] bytes, string label, string kind)
        {
            if (IsRealTcpMode && IsLoopbackRunning)
            {
                await _loopback.SendAsync(bytes);
                return;
            }

            ProcessIncomingChunk(bytes, label, kind);
        }

        private void SendSingleFrameInternal(string payload, string label, string kind)
        {
            var raw = BuildFrameForCurrentProtocol(payload, _txSeq++);
            ProcessIncomingChunk(raw, label, kind);
        }

        private byte[] BuildFrameForCurrentProtocol(string payload, byte seq) => SelectedProtocol.Kind switch
        {
            FramingProtocolKind.AsciiDelimiterCrLf => CustomProtocolCodec.BuildAsciiFrame(payload),
            FramingProtocolKind.FixedLength12Byte => CustomProtocolCodec.BuildFixed12Frame(seq, 24.5f + (seq % 7) * 0.35f, 38.0f + (seq % 5) * 0.4f),
            _ => CustomProtocolCodec.BuildBinaryFrame(cmd: 0x01, seq, payload),
        };

        private void ProcessIncomingChunk(byte[] bytes, string label, string kind)
        {
            var chunkRec = new StreamChunkRecord(_nextChunkId++, DateTime.Now, bytes, label, kind);
            RawChunks.Insert(0, chunkRec);
            while (RawChunks.Count > MaxLogEntries) RawChunks.RemoveAt(RawChunks.Count - 1);

            _canvasChunks.Add(chunkRec);
            if (_canvasChunks.Count > 6) _canvasChunks.RemoveAt(0);

            var decoded = _decoder.Feed(bytes);
            foreach (var f in decoded)
            {
                DecodedFrames.Insert(0, f);
                while (DecodedFrames.Count > MaxLogEntries) DecodedFrames.RemoveAt(DecodedFrames.Count - 1);
                _canvasFrames.Add(f);
                if (_canvasFrames.Count > 6) _canvasFrames.RemoveAt(0);
            }

            var pending = _decoder.GetPendingBufferCopy();
            ValidFrameCount = _decoder.ValidFrameCount;
            CrcErrorCount = _decoder.CrcErrorCount;
            GarbageBytesDiscarded = _decoder.GarbageBytesDiscarded;
            PendingBufferBytes = pending.Length;

            PipelineSnapshot = new FramingPipelineSnapshot(
                _decoder.Protocol, [.. _canvasChunks], pending, [.. _canvasFrames],
                ValidFrameCount, CrcErrorCount, GarbageBytesDiscarded);

            DeliverNextFragmentCommand.NotifyCanExecuteChanged();
        }

        private void ClearBuffersOnly()
        {
            _pendingFragments.Clear();
            PendingFragmentCount = 0;
            _canvasChunks.Clear();
            _canvasFrames.Clear();
            RawChunks.Clear();
            DecodedFrames.Clear();
            ValidFrameCount = 0;
            CrcErrorCount = 0;
            GarbageBytesDiscarded = 0;
            PendingBufferBytes = 0;
            PipelineSnapshot = new FramingPipelineSnapshot(_decoder.Protocol, [], [], [], 0, 0, 0);
            DeliverNextFragmentCommand.NotifyCanExecuteChanged();
        }

        private void SeedInitialRs485History()
        {
            RecordBusEvent("01 扫码枪", "TX", "Master → 01 触发读码", 0.7, false);
            RecordBusEvent("01 扫码枪", "RX", "01 → Master: SN:B260118", 2.9, false);
            RecordBusEvent("02 激光测距", "TX", "Master → 02 读取位移", 0.7, false);
            RecordBusEvent("02 激光测距", "RX", "02 → Master: 14.285 mm", 3.1, false);
            Rs485SuccessCount = 2;
        }

        private void RecordBusEvent(string device, string direction, string detail, double latencyMs, bool isError)
        {
            var ev = new Rs485BusEvent(_busEventId++, DateTime.Now, device, direction, detail, Math.Round(latencyMs, 2), isError);
            Rs485EventLog.Insert(0, ev);
            while (Rs485EventLog.Count > MaxLogEntries) Rs485EventLog.RemoveAt(Rs485EventLog.Count - 1);

            _canvasBusEvents.Add(ev);
            if (_canvasBusEvents.Count > 10) _canvasBusEvents.RemoveAt(0);
            Rs485BusEvents = [.. _canvasBusEvents];
        }

        private void RecalculateSerialTiming()
        {
            SerialTiming = SerialTimingCalculator.Calculate(
                SelectedBaudRate.BaudRate,
                SelectedSerialFormat.DataBits,
                SelectedSerialFormat.HasParity,
                SelectedSerialFormat.StopBits,
                TxBytesCount,
                RxBytesCount,
                TurnaroundMs);
        }

        private static byte[] Concat(params byte[][] arrays)
        {
            var total = arrays.Sum(a => a.Length);
            var res = new byte[total];
            var offset = 0;
            foreach (var a in arrays)
            {
                Buffer.BlockCopy(a, 0, res, offset, a.Length);
                offset += a.Length;
            }

            return res;
        }

        private void RunOnUi(Action action)
        {
            if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
            {
                _uiContext.Post(_ => action(), null);
            }
            else
            {
                action();
            }
        }
    }
}
