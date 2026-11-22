using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.OpcUa;
using LearnSmartManufacturing.Services.OpcUa;

namespace LearnSmartManufacturing.ViewModels
{
    public sealed record OpcUaSecurityOption(OpcUaSecurityMode Mode, string Name);

    public partial class OpcUaTreeNodeViewModel : ObservableObject
    {
        private readonly Action<OpcUaTreeNodeViewModel> _onSelected;

        public OpcUaTreeNodeViewModel(OpcUaNodeSnapshot snapshot, Action<OpcUaTreeNodeViewModel> onSelected)
        {
            _onSelected = onSelected;
            Snapshot = snapshot;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NodeId))]
        [NotifyPropertyChangedFor(nameof(DisplayName))]
        [NotifyPropertyChangedFor(nameof(ClassTag))]
        [NotifyPropertyChangedFor(nameof(InlineValueText))]
        [NotifyPropertyChangedFor(nameof(IsGoodStatus))]
        private OpcUaNodeSnapshot snapshot;

        [ObservableProperty]
        private bool isExpanded = true;

        [ObservableProperty]
        private bool isSelected;

        public ObservableCollection<OpcUaTreeNodeViewModel> Children { get; } = [];

        public string NodeId => Snapshot.NodeId;
        public string DisplayName => Snapshot.DisplayName;
        public string ClassTag => Snapshot.ClassTag;
        public bool IsGoodStatus => Snapshot.IsGoodStatus;

        public string InlineValueText => Snapshot.NodeClass switch
        {
            OpcUaNodeClass.Object => string.Empty,
            OpcUaNodeClass.Method => "[RPC]",
            _ => string.IsNullOrEmpty(Snapshot.EngineeringUnit)
                ? Snapshot.ValueText
                : $"{Snapshot.ValueText} {Snapshot.EngineeringUnit}",
        };

        partial void OnIsSelectedChanged(bool value)
        {
            if (value)
            {
                _onSelected(this);
            }
        }
    }

    /// <summary>
    /// Industrial OPC UA 1.05 Workbench ViewModel:
    /// - Tab 1: UACP SecureChannel / X.509 Certificate Trust Handshake + Address Space Tree + Read/Write/Method RPC
    /// - Tab 2: Subscription / MonitoredItem Sampling, Absolute Deadband Filtering, KeepAlive &amp; OPC DA Comparison
    /// </summary>
    public partial class OpcUaViewModel : ObservableObject, IDisposable
    {
        private const int MaxSamples = 60;
        private const int MaxNotifications = 80;
        private const int MaxLogs = 100;

        private readonly SimulatedOpcUaServer _server = new();
        private readonly Dictionary<string, OpcUaTreeNodeViewModel> _treeLookup = new(StringComparer.Ordinal);
        private readonly Queue<OpcUaMonitoredSample> _sampleWindow = new(MaxSamples);

        private CancellationTokenSource? _subCts;
        private bool _disposed;

        public OpcUaViewModel()
        {
            SecurityOptions =
            [
                new(OpcUaSecurityMode.None, "None (明文测试 · 无加密)"),
                new(OpcUaSecurityMode.Sign, "Basic256Sha256 · Sign (签名防篡改)"),
                new(OpcUaSecurityMode.SignAndEncrypt, "Basic256Sha256 · SignAndEncrypt (工业推荐)"),
            ];

            _server.HandshakeRecorded += t => LatestTrace = t;
            _server.NotificationPublished += OnNotificationPublished;
            _server.Logged += OnServerLogged;

            SelectedSecurityMode = SecurityOptions[2]; // SignAndEncrypt
            BuildAddressSpaceTree();

            // Connect synchronously & pre-populate 24 samples so Tab 1 & Tab 2 are rich on first open
            _server.Connect(EndpointUrl, SelectedSecurityMode.Mode);
            SyncConnectionAndTree();
            for (var i = 0; i < 24; i++)
            {
                StepSubscriptionInternal(injectStep: false);
            }

            if (_treeLookup.TryGetValue(SimulatedOpcUaServer.TempNodeId, out var tempNode))
            {
                tempNode.IsSelected = true;
            }

            StatusBanner = "OPC UA 1.05 模拟服务器已连接 (Basic256Sha256 · SignAndEncrypt)，可浏览地址空间、测试证书拦截或调节死区观察省流效果。";
        }

        public IReadOnlyList<OpcUaSecurityOption> SecurityOptions { get; }
        public ObservableCollection<OpcUaTreeNodeViewModel> RootNodes { get; } = [];
        public ObservableCollection<OpcUaNotificationRecord> Notifications { get; } = [];
        public ObservableCollection<OpcUaLogEntry> ProtocolLogs { get; } = [];

        #region Connection & Security

        [ObservableProperty]
        private string endpointUrl = SimulatedOpcUaServer.DefaultEndpointUrl;

        [ObservableProperty]
        private OpcUaSecurityOption selectedSecurityMode = null!;

        [ObservableProperty]
        private bool isClientCertificateTrusted = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsConnected))]
        [NotifyPropertyChangedFor(nameof(IsCertRejected))]
        [NotifyPropertyChangedFor(nameof(ConnectionBadgeText))]
        [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
        private OpcUaConnectionState connectionState = OpcUaConnectionState.Disconnected;

        [ObservableProperty]
        private OpcUaHandshakeTrace? latestTrace;

        [ObservableProperty]
        private string statusBanner = string.Empty;

        public bool IsConnected => ConnectionState == OpcUaConnectionState.Connected;
        public bool IsCertRejected => ConnectionState == OpcUaConnectionState.CertificateRejected;
        public string ConnectButtonText => ConnectionState == OpcUaConnectionState.Connected ? "断开会话" : "连接服务器";

        public string ConnectionBadgeText => ConnectionState switch
        {
            OpcUaConnectionState.Connected => $"CONNECTED · {SelectedSecurityMode?.Mode}",
            OpcUaConnectionState.CertificateRejected => "CERT REJECTED · 0x801A0000",
            _ => "DISCONNECTED",
        };

        #endregion

        #region Tab 1: Address Space Inspector & Read/Write/Method RPC

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanWriteSelectedNode))]
        [NotifyPropertyChangedFor(nameof(CanCallSelectedMethod))]
        private OpcUaNodeSnapshot? selectedNode;

        [ObservableProperty]
        private string writeValueText = "195.0";

        [ObservableProperty]
        private string lastOperationBanner = "选中左侧节点树任意变量或方法，测试 Read / Write（写只读节点返回 BadNotWritable）或远程 Call Method。";

        [ObservableProperty]
        private bool isLastOperationSuccess = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SensorFaultButtonText))]
        private bool isSensorFaultInjected;

        public bool CanWriteSelectedNode => SelectedNode is { NodeClass: OpcUaNodeClass.Variable };
        public bool CanCallSelectedMethod => SelectedNode is { NodeClass: OpcUaNodeClass.Method };
        public string SensorFaultButtonText => IsSensorFaultInjected ? "恢复热电偶正常 (Good)" : "注入传感器断线 (0x808F0000)";

        #endregion

        #region Tab 2: Subscription, Deadband & KeepAlive

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SamplingIntervalText))]
        private int samplingIntervalMs = 100;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PublishingIntervalText))]
        private int publishingIntervalMs = 500;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DeadbandText))]
        private double deadbandAbsolute = 0.6;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SubscriptionButtonText))]
        private bool isSubscriptionRunning;

        [ObservableProperty]
        private IReadOnlyList<OpcUaMonitoredSample> monitoredSamples = [];

        [ObservableProperty]
        private int totalSamplesCount;

        [ObservableProperty]
        private int filteredByDeadbandCount;

        [ObservableProperty]
        private int publishedNotificationsCount;

        [ObservableProperty]
        private int keepAliveCount;

        [ObservableProperty]
        private string bandwidthSavingsText = "0.0%";

        public string SamplingIntervalText => $"{SamplingIntervalMs} ms";
        public string PublishingIntervalText => $"{PublishingIntervalMs} ms";
        public string DeadbandText => DeadbandAbsolute <= 0.01 ? "0.0 °C (无过滤)" : $"±{DeadbandAbsolute:F1} °C";
        public string SubscriptionButtonText => IsSubscriptionRunning ? "停止实时订阅" : "启动实时订阅";

        #endregion

        partial void OnIsClientCertificateTrustedChanged(bool value)
        {
            _server.IsClientCertificateTrusted = value;
        }

        partial void OnSamplingIntervalMsChanged(int value) { _server.SamplingIntervalMs = value; }
        partial void OnPublishingIntervalMsChanged(int value) { _server.PublishingIntervalMs = value; }
        partial void OnDeadbandAbsoluteChanged(double value) { _server.DeadbandAbsolute = value; }

        [RelayCommand]
        private void ToggleConnection()
        {
            if (ConnectionState == OpcUaConnectionState.Connected)
            {
                StopSubscriptionLoop();
                _server.Disconnect();
                SyncConnectionAndTree();
                StatusBanner = "已安全关闭 OPC UA 会话与安全通道。";
                return;
            }

            var res = _server.Connect(EndpointUrl, SelectedSecurityMode.Mode);
            SyncConnectionAndTree();
            StatusBanner = res.Summary;
        }

        [RelayCommand]
        private void DemoUntrustedCertificate()
        {
            StopSubscriptionLoop();
            _server.Disconnect();
            SelectedSecurityMode = SecurityOptions[2]; // SignAndEncrypt
            IsClientCertificateTrusted = false;
            var res = _server.Connect(EndpointUrl, SelectedSecurityMode.Mode);
            SyncConnectionAndTree();
            StatusBanner = res.Summary;
        }

        [RelayCommand]
        private void TrustCertificateAndReconnect()
        {
            IsClientCertificateTrusted = true;
            var res = _server.Connect(EndpointUrl, SelectedSecurityMode.Mode);
            SyncConnectionAndTree();
            StatusBanner = res.Summary;
        }

        [RelayCommand]
        private void ReadSelectedNode()
        {
            if (SelectedNode is null) return;
            var res = _server.ReadNode(SelectedNode.NodeId);
            ApplyOperationResult(res);
            SyncConnectionAndTree();
        }

        [RelayCommand]
        private void WriteSelectedNode()
        {
            if (SelectedNode is null) return;
            var res = _server.WriteNode(SelectedNode.NodeId, WriteValueText);
            ApplyOperationResult(res);
            SyncConnectionAndTree();
        }

        [RelayCommand]
        private void CallSelectedMethod()
        {
            if (SelectedNode is not { NodeClass: OpcUaNodeClass.Method }) return;
            var res = _server.CallMethod(SelectedNode.NodeId);
            ApplyOperationResult(res);
            SyncConnectionAndTree();
        }

        [RelayCommand]
        private void QuickCallMethod(string? methodNodeId)
        {
            if (string.IsNullOrWhiteSpace(methodNodeId)) return;
            var res = _server.CallMethod(methodNodeId);
            ApplyOperationResult(res);
            SyncConnectionAndTree();
        }

        [RelayCommand]
        private void ToggleSensorFault()
        {
            _server.SetSensorFault(!_server.IsSensorFaultInjected);
            // Pump 1 sample burst to immediately show quality-code push bypassing Deadband!
            for (var i = 0; i < Math.Max(1, PublishingIntervalMs / Math.Max(50, SamplingIntervalMs)); i++)
            {
                StepSubscriptionInternal(injectStep: false);
            }

            SyncConnectionAndTree();
            StatusBanner = _server.IsSensorFaultInjected
                ? "已注入热电偶断线：Zone1Temp 质量码变为 BadSensorFailure (0x808F0000)，并绕过死区立即推送！"
                : "已恢复热电偶正常：Zone1Temp 质量码恢复 Good (0x00000000)。";
        }

        [RelayCommand]
        private void ToggleSubscription()
        {
            if (IsSubscriptionRunning)
            {
                StopSubscriptionLoop();
                StatusBanner = "已暂停实时订阅采样。";
                return;
            }

            if (ConnectionState != OpcUaConnectionState.Connected)
            {
                StatusBanner = "请先连接 OPC UA Server 再启动订阅。";
                return;
            }

            IsSubscriptionRunning = true;
            _subCts = new CancellationTokenSource();
            StatusBanner = $"已启动 OPC UA Subscription（采样 {SamplingIntervalMs}ms / 发布 {PublishingIntervalMs}ms / 死区 ±{DeadbandAbsolute:F1}°C）。";
            _ = RunSubscriptionLoopAsync(_subCts.Token);
        }

        [RelayCommand]
        private void StepSample()
        {
            StepSubscriptionInternal(injectStep: false);
            SyncConnectionAndTree();
        }

        [RelayCommand]
        private void InjectTempStep()
        {
            // Inject a +3.2 C step and run enough samples to hit a publish boundary so DataChange fires visibly
            StepSubscriptionInternal(injectStep: true);
            var span = Math.Max(1, PublishingIntervalMs / Math.Max(50, SamplingIntervalMs));
            for (var i = 0; i < span; i++)
            {
                StepSubscriptionInternal(injectStep: false);
            }

            SyncConnectionAndTree();
            StatusBanner = "已注入 +3.2 °C 炉温跃变（突破死区阈值），服务端立即打包 DataChangeNotification 推送！";
        }

        [RelayCommand]
        private void ResetSubscriptionStats()
        {
            _server.ResetSubscriptionStats();
            _sampleWindow.Clear();
            Notifications.Clear();
            MonitoredSamples = [];
            TotalSamplesCount = 0;
            FilteredByDeadbandCount = 0;
            PublishedNotificationsCount = 0;
            KeepAliveCount = 0;
            BandwidthSavingsText = "0.0%";
        }

        [RelayCommand]
        private void ClearLogs()
        {
            ProtocolLogs.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopSubscriptionLoop();
        }

        private async Task RunSubscriptionLoopAsync(CancellationToken ct)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(160));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    if (ConnectionState != OpcUaConnectionState.Connected) break;
                    StepSubscriptionInternal(injectStep: false);
                    SyncConnectionAndTree();
                }
            }
            catch (OperationCanceledException)
            {
                // Normal stop.
            }
            finally
            {
                IsSubscriptionRunning = false;
            }
        }

        private void StopSubscriptionLoop()
        {
            if (_subCts is not null)
            {
                _subCts.Cancel();
                _subCts.Dispose();
                _subCts = null;
            }

            IsSubscriptionRunning = false;
        }

        private void StepSubscriptionInternal(bool injectStep)
        {
            var sample = _server.StepSubscriptionSample(injectStep);
            if (_sampleWindow.Count >= MaxSamples)
            {
                _sampleWindow.Dequeue();
            }

            _sampleWindow.Enqueue(sample);
            MonitoredSamples = [.. _sampleWindow];

            TotalSamplesCount = _server.TotalSamplesCount;
            FilteredByDeadbandCount = _server.FilteredByDeadbandCount;
            PublishedNotificationsCount = _server.PublishedNotificationsCount;
            KeepAliveCount = _server.KeepAliveCount;

            var savings = TotalSamplesCount > 0
                ? Math.Clamp((1.0 - PublishedNotificationsCount / (double)TotalSamplesCount) * 100.0, 0, 100)
                : 0;
            BandwidthSavingsText = $"{savings:F1}%";
        }

        private void BuildAddressSpaceTree()
        {
            RootNodes.Clear();
            _treeLookup.Clear();
            var all = _server.GetAllNodes();
            foreach (var snap in all)
            {
                var vm = new OpcUaTreeNodeViewModel(snap, OnTreeNodeSelected);
                _treeLookup[snap.NodeId] = vm;
            }

            foreach (var snap in all)
            {
                var vm = _treeLookup[snap.NodeId];
                if (string.IsNullOrEmpty(snap.ParentNodeId))
                {
                    RootNodes.Add(vm);
                }
                else if (_treeLookup.TryGetValue(snap.ParentNodeId, out var parent))
                {
                    parent.Children.Add(vm);
                }
            }
        }

        private void SyncConnectionAndTree()
        {
            ConnectionState = _server.State;
            IsSensorFaultInjected = _server.IsSensorFaultInjected;

            foreach (var snap in _server.GetAllNodes())
            {
                if (_treeLookup.TryGetValue(snap.NodeId, out var vm))
                {
                    vm.Snapshot = snap;
                    if (SelectedNode?.NodeId == snap.NodeId)
                    {
                        SelectedNode = snap;
                    }
                }
            }
        }

        private void OnTreeNodeSelected(OpcUaTreeNodeViewModel nodeVm)
        {
            SelectedNode = nodeVm.Snapshot;
            if (nodeVm.Snapshot.NodeClass == OpcUaNodeClass.Variable)
            {
                WriteValueText = nodeVm.Snapshot.ValueText;
            }
        }

        private void ApplyOperationResult(OpcUaOperationResult res)
        {
            IsLastOperationSuccess = res.Success;
            LastOperationBanner = res.Success
                ? $"✓ {res.Summary}"
                : $"✕ {res.Summary}";
        }

        private void OnNotificationPublished(OpcUaNotificationRecord rec)
        {
            Notifications.Insert(0, rec);
            while (Notifications.Count > MaxNotifications)
            {
                Notifications.RemoveAt(Notifications.Count - 1);
            }
        }

        private void OnServerLogged(OpcUaLogEntry entry)
        {
            ProtocolLogs.Insert(0, entry);
            while (ProtocolLogs.Count > MaxLogs)
            {
                ProtocolLogs.RemoveAt(ProtocolLogs.Count - 1);
            }
        }
    }
}
