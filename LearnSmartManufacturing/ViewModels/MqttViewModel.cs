using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.Mqtt;
using LearnSmartManufacturing.Services.Mqtt;

namespace LearnSmartManufacturing.ViewModels
{
    public sealed record MqttTransportOption(MqttTransportMode Mode, string Name);

    public sealed record MqttQosOption(MqttQos Qos, string Name, string Summary);

    public sealed record MqttTopicPreset(string Name, string Topic, string Payload, MqttQos DefaultQos, bool DefaultRetain);

    /// <summary>
    /// Industrial MQTT 3.1.1 workbench ViewModel supporting an in-memory simulated Broker
    /// (QoS 0/1/2 handshakes, Retain, LWT, Store-and-Forward replay, ISA-95 wildcard testing)
    /// as well as a real TCP MQTT 3.1.1 client (Mosquitto / EMQX).
    /// </summary>
    public partial class MqttViewModel : ObservableObject, IDisposable
    {
        private const string DefaultStatusTopic = "factory/line1/smt-01/status";
        private const string DefaultLwtPayload = "{\"online\":false,\"reason\":\"lwt_unexpected_drop\"}";
        private const string DefaultOnlinePayload = "{\"online\":true,\"line\":\"line1\",\"device\":\"smt-01\"}";
        private const int MaxMessages = 100;
        private const int MaxLogs = 120;

        private static readonly string[] SampleIndustrialTopics =
        [
            "factory/line1/smt-01/telemetry",
            "factory/line1/smt-01/status",
            "factory/line1/smt-01/alarm",
            "factory/line1/aoi-02/telemetry",
            "factory/line1/smt-01/cmd/recipe",
            "factory/line1",
            "factory/line2/cnc-01/telemetry",
            "factory/line2/cnc-01/status",
            "$SYS/broker/clients/connected",
        ];

        private readonly InMemoryMqttBroker _broker = new();
        private readonly TcpMqttClient _tcpClient = new();
        private readonly SynchronizationContext? _uiContext;
        private readonly bool _isInitialized;

        private CancellationTokenSource? _autoPublishCts;
        private int _telemetrySeq;
        private bool _disposed;

        public MqttViewModel()
        {
            _uiContext = SynchronizationContext.Current;

            TransportModes =
            [
                new MqttTransportOption(MqttTransportMode.SimulatedBroker, "内置模拟 Broker (零依赖闭环)"),
                new MqttTransportOption(MqttTransportMode.RealTcpBroker, "真实 TCP Broker (Mosquitto/EMQX 1883)"),
            ];

            QosOptions =
            [
                new MqttQosOption(MqttQos.AtMostOnce, "QoS 0 · 最多一次", "发完即忘，无 ACK、断网不缓存，适合高频遥测"),
                new MqttQosOption(MqttQos.AtLeastOnce, "QoS 1 · 至少一次", "PUBLISH → PUBACK 两步确认，丢包触发 DUP=1 重传（可能重复）"),
                new MqttQosOption(MqttQos.ExactlyOnce, "QoS 2 · 恰好一次", "PUBLISH→PUBREC→PUBREL→PUBCOMP 四步握手（不丢不重）"),
            ];

            TopicPresets =
            [
                new MqttTopicPreset("SMT 炉温遥测 (Telemetry)", "factory/line1/smt-01/telemetry",
                    "{\"seq\":1,\"tempC\":186.4,\"rpm\":1480,\"oee\":0.95}", MqttQos.AtMostOnce, false),
                new MqttTopicPreset("设备在线状态 (Status · Retain)", DefaultStatusTopic,
                    DefaultOnlinePayload, MqttQos.AtLeastOnce, true),
                new MqttTopicPreset("超温紧急报警 (Alarm · QoS 2)", "factory/line1/smt-01/alarm",
                    "{\"code\":\"E402\",\"level\":\"CRITICAL\",\"msg\":\"Zone3 over-temp 262C\"}", MqttQos.ExactlyOnce, false),
                new MqttTopicPreset("AOI 视觉良率 (Telemetry)", "factory/line1/aoi-02/telemetry",
                    "{\"inspected\":1204,\"ng\":3,\"yield\":0.9975}", MqttQos.AtLeastOnce, false),
                new MqttTopicPreset("二号线 CNC 状态 (Retain)", "factory/line2/cnc-01/status",
                    "{\"online\":true,\"spindleRpm\":6000}", MqttQos.AtLeastOnce, true),
                new MqttTopicPreset("MES 配方下发 (Cmd · QoS 2)", "factory/line1/smt-01/cmd/recipe",
                    "{\"recipe\":\"PCB-A09\",\"targetTemp\":190.0}", MqttQos.ExactlyOnce, false),
                new MqttTopicPreset("清除保留消息 (空载荷 Retain)", "factory/line1/smt-01/telemetry",
                    string.Empty, MqttQos.AtLeastOnce, true),
            ];

            // Subscribe to broker & real TCP client events
            _broker.HandshakeRecorded += OnBrokerHandshakeRecorded;
            _broker.MessageDelivered += OnMessageDelivered;
            _broker.ProtocolLogged += OnProtocolLogged;

            _tcpClient.MessageReceived += msg => RunOnUi(() => OnMessageDelivered(msg));
            _tcpClient.ProtocolLogged += log => RunOnUi(() => OnProtocolLogged(log));
            _tcpClient.Disconnected += reason => RunOnUi(() => HandleRealTcpUnexpectedDrop(reason));

            // Initialize selections after all option arrays exist
            SelectedTransportMode = TransportModes[0];
            SelectedQos = QosOptions[1];
            SelectedSubQos = QosOptions[1];
            SelectedTopicPreset = TopicPresets[0];

            // Connect simulated broker synchronously with LWT + initial industrial subscriptions & retained state
            ConnectSimulatedInitial();
            EvaluateWildcardLab();

            _isInitialized = true;
        }

        public IReadOnlyList<MqttTransportOption> TransportModes { get; }
        public IReadOnlyList<MqttQosOption> QosOptions { get; }
        public IReadOnlyList<MqttTopicPreset> TopicPresets { get; }

        public ObservableCollection<MqttReceivedMessage> ReceivedMessages { get; } = [];
        public ObservableCollection<MqttSubscriptionEntry> Subscriptions { get; } = [];
        public ObservableCollection<MqttRetainedEntry> RetainedEntries { get; } = [];
        public ObservableCollection<MqttProtocolLog> ProtocolLogs { get; } = [];
        public ObservableCollection<TopicMatchTestRow> TopicMatchRows { get; } = [];

        public string LwtTopic => DefaultStatusTopic;
        public string LwtPayload => DefaultLwtPayload;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSimulatedMode))]
        [NotifyPropertyChangedFor(nameof(IsRealTcpMode))]
        private MqttTransportOption selectedTransportMode = null!;

        [ObservableProperty]
        private string host = "127.0.0.1";

        [ObservableProperty]
        private string portText = "1883";

        [ObservableProperty]
        private string clientId = "edge-line1-gw";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsConnected))]
        [NotifyPropertyChangedFor(nameof(IsNetworkLost))]
        [NotifyPropertyChangedFor(nameof(CanSimulateNetworkDrop))]
        [NotifyPropertyChangedFor(nameof(CanReconnectNetwork))]
        [NotifyPropertyChangedFor(nameof(ConnectionBadgeText))]
        [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
        private MqttConnectionState connectionState = MqttConnectionState.Disconnected;

        [ObservableProperty]
        private string connectionStatusMessage = "正在初始化内置模拟 MQTT 3.1.1 Broker...";

        [ObservableProperty]
        private MqttTopicPreset? selectedTopicPreset;

        [ObservableProperty]
        private string publishTopic = "factory/line1/smt-01/telemetry";

        [ObservableProperty]
        private string publishPayload = "{\"seq\":1,\"tempC\":186.4,\"rpm\":1480,\"oee\":0.95}";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedQosSummary))]
        private MqttQosOption selectedQos = null!;

        [ObservableProperty]
        private bool retainFlag;

        [ObservableProperty]
        private bool simulatePacketDrop;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AutoPublishButtonText))]
        private bool isAutoPublishing;

        [ObservableProperty]
        private string newSubscriptionFilter = "factory/line1/#";

        [ObservableProperty]
        private MqttQosOption selectedSubQos = null!;

        [ObservableProperty]
        private MqttHandshakeTrace? latestTrace;

        [ObservableProperty]
        private long publishedCount;

        [ObservableProperty]
        private long receivedCount;

        [ObservableProperty]
        private int retainedCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(OfflineBufferSummary))]
        private int offlineBufferedCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(OfflineBufferSummary))]
        private int replayedCount;

        [ObservableProperty]
        private bool isDeviceOnline = true;

        [ObservableProperty]
        private string deviceOnlineStatusText = "在线 (Birth 保留消息已上报)";

        [ObservableProperty]
        private string testFilterText = "factory/line1/+/telemetry";

        [ObservableProperty]
        private bool isTestFilterValid = true;

        [ObservableProperty]
        private string testFilterStatusText = string.Empty;

        [ObservableProperty]
        private string lastActionHint = string.Empty;

        public bool IsSimulatedMode => SelectedTransportMode?.Mode == MqttTransportMode.SimulatedBroker;
        public bool IsRealTcpMode => SelectedTransportMode?.Mode == MqttTransportMode.RealTcpBroker;
        public bool IsConnected => ConnectionState == MqttConnectionState.Connected;
        public bool IsNetworkLost => ConnectionState == MqttConnectionState.NetworkLost;
        public bool CanSimulateNetworkDrop => IsSimulatedMode && ConnectionState == MqttConnectionState.Connected;
        public bool CanReconnectNetwork => IsSimulatedMode && ConnectionState == MqttConnectionState.NetworkLost;
        public string SelectedQosSummary => SelectedQos?.Summary ?? string.Empty;
        public string OfflineBufferSummary => $"{OfflineBufferedCount} / {ReplayedCount}";
        public string AutoPublishButtonText => IsAutoPublishing ? "停止 1s 遥测" : "启动 1s 遥测";

        public string ConnectionBadgeText => ConnectionState switch
        {
            MqttConnectionState.Connected => IsSimulatedMode ? "CONNECTED · 模拟 Broker" : $"CONNECTED · {Host}:{PortText}",
            MqttConnectionState.NetworkLost => "NETWORK LOST · LWT 已触发",
            _ => "DISCONNECTED",
        };

        public string ConnectButtonText => ConnectionState == MqttConnectionState.Disconnected ? "连接 Broker" : "正常断开";

        partial void OnSelectedTransportModeChanged(MqttTransportOption value)
        {
            if (!_isInitialized || value is null)
            {
                return;
            }

            StopAutoPublish();
            if (ConnectionState != MqttConnectionState.Disconnected)
            {
                _broker.DisconnectGracefully();
                _tcpClient.Disconnect();
                ConnectionState = MqttConnectionState.Disconnected;
            }

            if (value.Mode == MqttTransportMode.SimulatedBroker)
            {
                ConnectSimulatedBroker();
            }
            else
            {
                SimulatePacketDrop = false;
                ConnectionStatusMessage = $"已切换至真实 TCP MQTT 3.1.1 客户端，请填写 Broker 地址 ({Host}:{PortText}) 后点击“连接 Broker”。";
            }

            NotifyCommandStates();
        }

        partial void OnSelectedTopicPresetChanged(MqttTopicPreset? value)
        {
            if (value is null)
            {
                return;
            }

            PublishTopic = value.Topic;
            PublishPayload = value.Payload;
            RetainFlag = value.DefaultRetain;
            var match = QosOptions.FirstOrDefault(q => q.Qos == value.DefaultQos);
            if (match is not null)
            {
                SelectedQos = match;
            }
        }

        partial void OnTestFilterTextChanged(string value)
        {
            EvaluateWildcardLab();
        }

        [RelayCommand]
        private async Task ToggleConnectionAsync()
        {
            if (ConnectionState != MqttConnectionState.Disconnected)
            {
                StopAutoPublish();
                if (IsSimulatedMode)
                {
                    _broker.DisconnectGracefully();
                    SyncBrokerState();
                    ConnectionStatusMessage = "已发送正常 DISCONNECT 报文（按 MQTT 3.1.1 规范，Broker 不会触发 LWT 遗嘱）。";
                }
                else
                {
                    _tcpClient.Disconnect();
                    ConnectionState = MqttConnectionState.Disconnected;
                    ConnectionStatusMessage = "已断开真实 MQTT TCP 连接。";
                }

                NotifyCommandStates();
                return;
            }

            if (IsSimulatedMode)
            {
                ConnectSimulatedBroker();
                NotifyCommandStates();
                return;
            }

            if (!int.TryParse(PortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            {
                ConnectionStatusMessage = "端口无效，请输入 1 ~ 65535。";
                return;
            }

            try
            {
                ConnectionStatusMessage = $"正在连接真实 MQTT Broker {Host}:{port} ...";
                var will = new MqttWillConfig(DefaultStatusTopic, DefaultLwtPayload, MqttQos.AtLeastOnce, true);
                await _tcpClient.ConnectAsync(Host.Trim(), port, ClientId.Trim(), will);
                ConnectionState = MqttConnectionState.Connected;
                ConnectionStatusMessage = $"已连接真实 Broker {Host}:{port}（ClientId={ClientId}，已注册 LWT 遗嘱）。";

                foreach (var sub in Subscriptions)
                {
                    await _tcpClient.SubscribeAsync(sub.Filter, sub.MaxQos);
                }
            }
            catch (Exception ex)
            {
                ConnectionState = MqttConnectionState.Disconnected;
                ConnectionStatusMessage = $"TCP 连接失败：{ex.Message}";
                OnProtocolLogged(new MqttProtocolLog(DateTime.Now, "Error", "CONNECT", ConnectionStatusMessage));
            }

            NotifyCommandStates();
        }

        [RelayCommand(CanExecute = nameof(CanSimulateNetworkDrop))]
        private void SimulateNetworkDrop()
        {
            if (!IsSimulatedMode || ConnectionState != MqttConnectionState.Connected)
            {
                return;
            }

            _broker.SimulateUnexpectedDisconnect();
            SyncBrokerState();
            ConnectionStatusMessage = "已模拟异常掉线（未发送 DISCONNECT）：Broker 已代发 LWT 遗嘱，当前 QoS≥1 发布将自动缓存等待重连续传。";
            NotifyCommandStates();
        }

        [RelayCommand(CanExecute = nameof(CanReconnectNetwork))]
        private void ReconnectNetwork()
        {
            if (!IsSimulatedMode || ConnectionState != MqttConnectionState.NetworkLost)
            {
                return;
            }

            var bufferedBefore = _broker.OfflineBufferedCount;
            var will = new MqttWillConfig(DefaultStatusTopic, DefaultLwtPayload, MqttQos.AtLeastOnce, true);
            _broker.Connect(ClientId, will);
            // Publish Birth online retained status after reconnect
            _broker.Publish(DefaultStatusTopic, "{\"online\":true,\"line\":\"line1\",\"reason\":\"reconnected_birth\"}",
                MqttQos.AtLeastOnce, retain: true, simulatePacketDrop: false, out _);

            SyncBrokerState();
            ConnectionStatusMessage = bufferedBefore > 0
                ? $"网络已恢复重连：自动补发了 {bufferedBefore} 条离线缓存消息，并重新上报上线 Birth 保留状态。"
                : "网络已恢复重连：已重新注册 LWT 并上报上线 Birth 保留状态。";
            NotifyCommandStates();
        }

        [RelayCommand]
        private async Task PublishAsync()
        {
            var topic = PublishTopic.Trim();
            var payload = PublishPayload ?? string.Empty;
            var qos = SelectedQos.Qos;

            if (IsSimulatedMode)
            {
                if (!_broker.Publish(topic, payload, qos, RetainFlag, SimulatePacketDrop, out var error))
                {
                    LastActionHint = error;
                    OnProtocolLogged(new MqttProtocolLog(DateTime.Now, "Error", "PUBLISH", error));
                    return;
                }

                LastActionHint = ConnectionState == MqttConnectionState.NetworkLost
                    ? (qos == MqttQos.AtMostOnce
                        ? "断网状态 QoS 0 已丢弃（协议不保证送达）。"
                        : $"断网状态 QoS {(int)qos} 已进入本地离线队列（待续传 {_broker.OfflineBufferedCount} 条）。")
                    : $"已发布至 {topic} (QoS {(int)qos}{(RetainFlag ? ", RETAIN" : string.Empty)})。";
                SyncBrokerState();
                return;
            }

            if (!_tcpClient.IsConnected)
            {
                LastActionHint = "请先连接真实 TCP Broker。";
                return;
            }

            if (!MqttTopicMatcher.IsValidTopicName(topic, out var topicErr))
            {
                LastActionHint = topicErr;
                return;
            }

            try
            {
                var raw = await _tcpClient.PublishAsync(topic, payload, qos, RetainFlag);
                PublishedCount++;
                LatestTrace = new MqttHandshakeTrace(DateTime.Now, $"真实 Broker 发布 {topic}", qos, raw,
                [
                    new MqttHandshakeStep($"PUBLISH (QoS {(int)qos})", true, $"{topic} · {raw.Length} B"),
                ]);
                LastActionHint = $"已发送 PUBLISH 至真实 Broker ({raw.Length} B)。";
            }
            catch (Exception ex)
            {
                LastActionHint = $"发布失败：{ex.Message}";
                OnProtocolLogged(new MqttProtocolLog(DateTime.Now, "Error", "PUBLISH", LastActionHint));
            }
        }

        [RelayCommand]
        private void ToggleAutoPublish()
        {
            if (IsAutoPublishing)
            {
                StopAutoPublish();
                LastActionHint = "已停止定时遥测发布。";
                return;
            }

            if (ConnectionState == MqttConnectionState.Disconnected)
            {
                LastActionHint = "请先连接 Broker 再启动定时遥测。";
                return;
            }

            IsAutoPublishing = true;
            _autoPublishCts = new CancellationTokenSource();
            var ct = _autoPublishCts.Token;
            LastActionHint = "已启动 1s 周期工业遥测上报（可在运行中点“模拟异常掉线”观察断网续传队列自动累积）。";
            _ = RunAutoPublishLoopAsync(ct);
        }

        [RelayCommand]
        private async Task SubscribeAsync()
        {
            var filter = NewSubscriptionFilter.Trim();
            var qos = SelectedSubQos.Qos;

            if (IsSimulatedMode)
            {
                if (!_broker.Subscribe(filter, qos, out var error))
                {
                    LastActionHint = error;
                    OnProtocolLogged(new MqttProtocolLog(DateTime.Now, "Error", "SUBSCRIBE", error));
                    return;
                }

                LastActionHint = $"已订阅过滤器 \"{filter}\" (最大 QoS {(int)qos})。";
                SyncBrokerState();
                return;
            }

            if (!MqttTopicMatcher.IsValidTopicFilter(filter, out var filterErr))
            {
                LastActionHint = filterErr;
                return;
            }

            if (!_tcpClient.IsConnected)
            {
                LastActionHint = "请先连接真实 TCP Broker。";
                return;
            }

            try
            {
                await _tcpClient.SubscribeAsync(filter, qos);
                if (Subscriptions.All(s => !string.Equals(s.Filter, filter, StringComparison.Ordinal)))
                {
                    Subscriptions.Add(new MqttSubscriptionEntry(filter, qos, 0));
                }

                LastActionHint = $"已向真实 Broker 发送 SUBSCRIBE \"{filter}\"。";
            }
            catch (Exception ex)
            {
                LastActionHint = $"订阅失败：{ex.Message}";
            }
        }

        [RelayCommand]
        private void Unsubscribe(string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return;
            }

            if (IsSimulatedMode)
            {
                _broker.Unsubscribe(filter);
                SyncBrokerState();
            }
            else
            {
                var item = Subscriptions.FirstOrDefault(s => string.Equals(s.Filter, filter, StringComparison.Ordinal));
                if (item is not null)
                {
                    Subscriptions.Remove(item);
                }
            }

            LastActionHint = $"已取消订阅 \"{filter}\"。";
        }

        [RelayCommand]
        private void ProbeNewDashboardRetained()
        {
            if (!IsSimulatedMode)
            {
                LastActionHint = "该演示仅在内置模拟 Broker 模式下可用。";
                return;
            }

            var snapshots = _broker.ProbeRetainedForNewSubscriber("factory/#");
            if (snapshots.Count == 0)
            {
                LastActionHint = "当前 Broker 中没有匹配 factory/# 的保留消息。";
                OnProtocolLogged(new MqttProtocolLog(DateTime.Now, "Warn", "RETAIN", LastActionHint));
                return;
            }

            foreach (var entry in snapshots)
            {
                OnMessageDelivered(new MqttReceivedMessage(
                    DateTime.Now, entry.Topic, entry.Payload, entry.Qos,
                    IsRetained: true, IsLwt: false, IsDuplicate: false, IsReplayed: false, MatchedFilter: "新上线看板(factory/#)"));
            }

            LastActionHint = $"模拟新打开的 MES 监控大屏订阅 factory/#：立即拉取到 {snapshots.Count} 条 Broker 保留快照（无需等待下次上报）。";
            OnProtocolLogged(new MqttProtocolLog(DateTime.Now, "Info", "RETAIN", LastActionHint));
        }

        [RelayCommand]
        private void ClearRetained(string? topic)
        {
            if (!IsSimulatedMode || string.IsNullOrWhiteSpace(topic))
            {
                return;
            }

            if (ConnectionState != MqttConnectionState.Connected)
            {
                LastActionHint = "需处于已连接状态才能发送空载荷 Retain 清除报文。";
                return;
            }

            _broker.Publish(topic, string.Empty, MqttQos.AtLeastOnce, retain: true, simulatePacketDrop: false, out _);
            SyncBrokerState();
            LastActionHint = $"已向 {topic} 发送空载荷 + Retain=true 报文，Broker 已移除该条保留消息。";
        }

        [RelayCommand]
        private void ApplyWildcardPreset(string? filter)
        {
            if (!string.IsNullOrEmpty(filter))
            {
                TestFilterText = filter;
            }
        }

        [RelayCommand]
        private async Task ApplyTestFilterToSubscribeAsync()
        {
            if (!IsTestFilterValid)
            {
                return;
            }

            NewSubscriptionFilter = TestFilterText.Trim();
            await SubscribeAsync();
        }

        [RelayCommand]
        private void ClearMessages()
        {
            ReceivedMessages.Clear();
            ReceivedCount = 0;
        }

        [RelayCommand]
        private void ClearLogs()
        {
            ProtocolLogs.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopAutoPublish();
            _tcpClient.Dispose();
        }

        private void ConnectSimulatedInitial()
        {
            var will = new MqttWillConfig(DefaultStatusTopic, DefaultLwtPayload, MqttQos.AtLeastOnce, true);
            _broker.Connect(ClientId, will);
            _broker.Subscribe("factory/line1/+/telemetry", MqttQos.AtLeastOnce, out _);
            _broker.Subscribe("factory/line1/+/status", MqttQos.AtLeastOnce, out _);
            _broker.Subscribe("factory/line1/+/alarm", MqttQos.ExactlyOnce, out _);

            _broker.Publish(DefaultStatusTopic, DefaultOnlinePayload, MqttQos.AtLeastOnce, retain: true, simulatePacketDrop: false, out _);
            _broker.Publish("factory/line1/smt-01/telemetry",
                "{\"seq\":0,\"tempC\":185.2,\"rpm\":1450,\"oee\":0.94}", MqttQos.AtLeastOnce, retain: true, simulatePacketDrop: false, out _);

            SyncBrokerState();
            ConnectionStatusMessage = "已连接内置模拟 MQTT 3.1.1 Broker（已注册 LWT 遗嘱、3 个 ISA-95 订阅与 2 条保留快照）。";
            LastActionHint = "提示：勾选“模拟网络丢包”后切换 QoS 0/1/2 点击发布，可直观对比丢包、DUP=1 重复与 4 步精确一次握手。";
        }

        private void ConnectSimulatedBroker()
        {
            var will = new MqttWillConfig(DefaultStatusTopic, DefaultLwtPayload, MqttQos.AtLeastOnce, true);
            _broker.Connect(ClientId, will);
            _broker.Publish(DefaultStatusTopic, DefaultOnlinePayload, MqttQos.AtLeastOnce, retain: true, simulatePacketDrop: false, out _);
            SyncBrokerState();
            ConnectionStatusMessage = $"已连接内置模拟 Broker（ClientId={ClientId}，已重新注册 LWT 遗嘱）。";
        }

        private async Task RunAutoPublishLoopAsync(CancellationToken ct)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    if (ConnectionState == MqttConnectionState.Disconnected)
                    {
                        break;
                    }

                    _telemetrySeq++;
                    var temp = Math.Round(184.5 + Math.Sin(_telemetrySeq * 0.45) * 3.8, 1);
                    var rpm = 1460 + (_telemetrySeq % 5) * 15;
                    var payload = string.Create(CultureInfo.InvariantCulture,
                        $"{{\"seq\":{_telemetrySeq},\"tempC\":{temp:F1},\"rpm\":{rpm},\"oee\":0.95}}");

                    if (IsSimulatedMode)
                    {
                        _broker.Publish("factory/line1/smt-01/telemetry", payload, SelectedQos.Qos, retain: false, simulatePacketDrop: false, out _);
                        SyncBrokerState();
                    }
                    else if (_tcpClient.IsConnected)
                    {
                        await _tcpClient.PublishAsync("factory/line1/smt-01/telemetry", payload, SelectedQos.Qos, retain: false, ct);
                        PublishedCount++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped normally.
            }
            catch (Exception ex)
            {
                OnProtocolLogged(new MqttProtocolLog(DateTime.Now, "Error", "AUTO-PUB", $"定时遥测异常停止：{ex.Message}"));
            }
            finally
            {
                IsAutoPublishing = false;
            }
        }

        private void StopAutoPublish()
        {
            if (_autoPublishCts is not null)
            {
                _autoPublishCts.Cancel();
                _autoPublishCts.Dispose();
                _autoPublishCts = null;
            }

            IsAutoPublishing = false;
        }

        private void SyncBrokerState()
        {
            ConnectionState = _broker.State;
            PublishedCount = _broker.PublishedCount;
            OfflineBufferedCount = _broker.OfflineBufferedCount;
            ReplayedCount = _broker.ReplayedCount;

            Subscriptions.Clear();
            foreach (var sub in _broker.GetSubscriptions())
            {
                Subscriptions.Add(sub);
            }

            RetainedEntries.Clear();
            foreach (var entry in _broker.GetRetainedMessages())
            {
                RetainedEntries.Add(entry);
            }

            RetainedCount = RetainedEntries.Count;
        }

        private void EvaluateWildcardLab()
        {
            TopicMatchRows.Clear();
            var filter = TestFilterText?.Trim() ?? string.Empty;

            if (!MqttTopicMatcher.IsValidTopicFilter(filter, out var error))
            {
                IsTestFilterValid = false;
                TestFilterStatusText = $"非法过滤器：{error}";
                foreach (var sample in SampleIndustrialTopics)
                {
                    TopicMatchRows.Add(new TopicMatchTestRow(sample, false, "过滤器语法错误"));
                }

                return;
            }

            IsTestFilterValid = true;
            var hitCount = 0;
            foreach (var sample in SampleIndustrialTopics)
            {
                var matched = MqttTopicMatcher.IsMatchWithParentMultiLevel(filter, sample);
                if (matched)
                {
                    hitCount++;
                }

                var reason = DescribeMatchReason(filter, sample, matched);
                TopicMatchRows.Add(new TopicMatchTestRow(sample, matched, reason));
            }

            TestFilterStatusText = $"合法过滤器 · 命中 {hitCount} / {SampleIndustrialTopics.Length} 个工业主题";
        }

        private static string DescribeMatchReason(string filter, string topic, bool matched)
        {
            if (topic.StartsWith('$') && !matched && (filter.StartsWith('#') || filter.StartsWith('+')))
            {
                return "§4.7.2: $ 系统主题不匹配前导通配符";
            }

            if (matched && filter.EndsWith("/#", StringComparison.Ordinal) &&
                string.Equals(filter[..^2], topic, StringComparison.Ordinal))
            {
                return "§4.7.1.2: # 包含父级自身主题";
            }

            if (matched)
            {
                return filter.Contains('#') ? "多层通配符 '#' 命中"
                    : filter.Contains('+') ? "单层通配符 '+' 命中"
                    : "精确主题匹配";
            }

            return "层级或主题名不匹配";
        }

        private void OnBrokerHandshakeRecorded(MqttHandshakeTrace trace)
        {
            LatestTrace = trace;
        }

        private void OnMessageDelivered(MqttReceivedMessage message)
        {
            ReceivedCount++;
            ReceivedMessages.Insert(0, message);
            while (ReceivedMessages.Count > MaxMessages)
            {
                ReceivedMessages.RemoveAt(ReceivedMessages.Count - 1);
            }

            if (string.Equals(message.Topic, DefaultStatusTopic, StringComparison.Ordinal))
            {
                if (message.IsLwt || message.Payload.Contains("\"online\":false", StringComparison.OrdinalIgnoreCase))
                {
                    IsDeviceOnline = false;
                    DeviceOnlineStatusText = message.IsLwt
                        ? "离线 · Broker 已自动发布 LWT 遗嘱"
                        : "离线 · 收到设备下线状态";
                }
                else if (message.Payload.Contains("\"online\":true", StringComparison.OrdinalIgnoreCase))
                {
                    IsDeviceOnline = true;
                    DeviceOnlineStatusText = "在线 · 设备运行正常 (Birth Retain)";
                }
            }
        }

        private void OnProtocolLogged(MqttProtocolLog log)
        {
            ProtocolLogs.Insert(0, log);
            while (ProtocolLogs.Count > MaxLogs)
            {
                ProtocolLogs.RemoveAt(ProtocolLogs.Count - 1);
            }
        }

        private void HandleRealTcpUnexpectedDrop(string reason)
        {
            StopAutoPublish();
            ConnectionState = MqttConnectionState.Disconnected;
            ConnectionStatusMessage = $"真实 TCP Broker 连接已中断：{reason}";
            OnProtocolLogged(new MqttProtocolLog(DateTime.Now, "Error", "TCP-DROP", ConnectionStatusMessage));
            NotifyCommandStates();
        }

        private void NotifyCommandStates()
        {
            SimulateNetworkDropCommand.NotifyCanExecuteChanged();
            ReconnectNetworkCommand.NotifyCanExecuteChanged();
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
