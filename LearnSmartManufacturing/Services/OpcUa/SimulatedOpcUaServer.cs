using System.Globalization;
using LearnSmartManufacturing.Models.OpcUa;

namespace LearnSmartManufacturing.Services.OpcUa
{
    /// <summary>
    /// Full-fidelity in-memory OPC UA 1.05 Server simulator implementing:
    /// - UACP SecureChannel (HEL/ACK/OPN) + X.509 Certificate Trust verification (BadCertificateUntrusted 0x801A0000)
    /// - Hierarchical Object-Oriented Address Space (Object / Variable / Method nodes with Engineering Units &amp; StatusCodes)
    /// - Read / Write / CallMethod services with BadNotWritable (0x803B0000) &amp; BadTypeMismatch (0x80740000)
    /// - Subscription &amp; MonitoredItem engine (SamplingInterval, PublishingInterval, Absolute Deadband, KeepAlive)
    /// </summary>
    public sealed class SimulatedOpcUaServer
    {
        private sealed class NodeRecord
        {
            public required string NodeId { get; init; }
            public required string BrowseName { get; init; }
            public required string DisplayName { get; init; }
            public required string ParentNodeId { get; init; }
            public required OpcUaNodeClass NodeClass { get; init; }
            public required string DataType { get; init; }
            public required bool IsWritable { get; init; }
            public required string EngineeringUnit { get; init; }
            public required string Description { get; init; }

            public string ValueText { get; set; } = string.Empty;
            public uint StatusCode { get; set; } = OpcUaStatusCodes.Good;
            public DateTime SourceTimestamp { get; set; } = DateTime.Now;
            public DateTime ServerTimestamp { get; set; } = DateTime.Now;
        }

        public const string DefaultEndpointUrl = "opc.tcp://127.0.0.1:4840/SmartFactory/UA";
        public const string TempNodeId = "ns=2;s=Line1.Oven.Zone1Temp";
        public const string TargetTempNodeId = "ns=2;s=Line1.Oven.TargetTemp";
        public const string OvenRunningNodeId = "ns=2;s=Line1.Oven.Running";
        public const string RecipeNodeId = "ns=2;s=Line1.Oven.RecipeName";
        public const string AlarmActiveNodeId = "ns=2;s=Line1.Oven.AlarmActive";
        public const string ResetAlarmMethodId = "ns=2;s=Line1.Oven.ResetAlarm";
        public const string YieldNodeId = "ns=2;s=Line1.Vision.YieldRate";
        public const string NgCountNodeId = "ns=2;s=Line1.Vision.NgCount";
        public const string TriggerInspectMethodId = "ns=2;s=Line1.Vision.TriggerInspect";

        private readonly Dictionary<string, NodeRecord> _nodes = new(StringComparer.Ordinal);
        private readonly List<string> _orderedNodeIds = [];
        private readonly Random _rng = new(4840);

        private double _currentTemp = 185.0;
        private double _targetTemp = 185.0;
        private int _inspectTotal = 1200;
        private int _inspectNg = 4;
        private int _sampleIndex;
        private double _lastReportedTemp = 185.0;
        private uint _lastReportedStatus = OpcUaStatusCodes.Good;
        private bool _hasPendingPublishChange;
        private double _pendingPublishTemp = 185.0;
        private uint _pendingPublishStatus = OpcUaStatusCodes.Good;
        private int _silentPublishCycles;
        private long _notificationSeq = 1;
        private uint _secureChannelId = 1001;

        public SimulatedOpcUaServer()
        {
            BuildAddressSpace();
        }

        public OpcUaConnectionState State { get; private set; } = OpcUaConnectionState.Disconnected;
        public OpcUaSecurityMode ActiveSecurityMode { get; private set; } = OpcUaSecurityMode.SignAndEncrypt;
        public bool IsClientCertificateTrusted { get; set; } = true;
        public bool IsSensorFaultInjected { get; private set; }

        // Subscription & MonitoredItem configuration
        public int SamplingIntervalMs { get; set; } = 100;
        public int PublishingIntervalMs { get; set; } = 500;
        public double DeadbandAbsolute { get; set; } = 0.6;
        public int MaxKeepAliveCount { get; set; } = 4;

        public int TotalSamplesCount { get; private set; }
        public int FilteredByDeadbandCount { get; private set; }
        public int PublishedNotificationsCount { get; private set; }
        public int KeepAliveCount { get; private set; }

        public event Action<OpcUaHandshakeTrace>? HandshakeRecorded;
        public event Action<OpcUaNotificationRecord>? NotificationPublished;
        public event Action<OpcUaLogEntry>? Logged;

        public IReadOnlyList<OpcUaNodeSnapshot> GetAllNodes() =>
            [.. _orderedNodeIds.Select(id => BuildSnapshot(_nodes[id]))];

        public OpcUaOperationResult Connect(string endpointUrl, OpcUaSecurityMode securityMode)
        {
            var url = string.IsNullOrWhiteSpace(endpointUrl) ? DefaultEndpointUrl : endpointUrl.Trim();
            ActiveSecurityMode = securityMode;
            var policyUri = securityMode == OpcUaSecurityMode.None
                ? "http://opcfoundation.org/UA/SecurityPolicy#None"
                : "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256";

            var hel = OpcUaBinaryCodec.BuildHelloPacket(url);
            var opn = OpcUaBinaryCodec.BuildOpenSecureChannelPacket(_secureChannelId++, policyUri);
            var encrypted = securityMode == OpcUaSecurityMode.SignAndEncrypt;

            var steps = new List<OpcUaHandshakeStep>
            {
                new("HEL", true, $"UACP Hello → {url}"),
                new("ACK", false, "Acknowledge (RecvBuf=64KB)"),
            };

            if (securityMode != OpcUaSecurityMode.None && !IsClientCertificateTrusted)
            {
                State = OpcUaConnectionState.CertificateRejected;
                steps.Add(new("OPN (X.509)", true, $"Basic256Sha256 · {securityMode}", IsEncrypted: encrypted));
                steps.Add(new("ERR 0x801A0000", false, "BadCertificateUntrusted · 拒绝建连", IsError: true));
                HandshakeRecorded?.Invoke(new OpcUaHandshakeTrace(DateTime.Now, "SecureChannel 握手失败：客户端证书未受信任", securityMode, opn, steps));
                var errMsg = "服务器拒绝 OpenSecureChannel：客户端 X.509 证书不在 TrustList 中 (BadCertificateUntrusted 0x801A0000)。";
                Log("Error", "OPN", errMsg);
                return new OpcUaOperationResult(false, OpcUaStatusCodes.BadCertificateUntrusted, errMsg);
            }

            State = OpcUaConnectionState.Connected;
            steps.Add(new("OPN", true, securityMode == OpcUaSecurityMode.None ? "SecurityPolicy#None" : $"Basic256Sha256 · X.509 ✓", IsEncrypted: encrypted));
            steps.Add(new("CreateSession", true, "SessionId=ns=1;g=4840-UA", IsEncrypted: encrypted));
            steps.Add(new("ActivateSession", false, "Session Activated · Good", IsEncrypted: encrypted));

            HandshakeRecorded?.Invoke(new OpcUaHandshakeTrace(
                DateTime.Now,
                $"OPC UA 安全通道与会话建立成功 ({securityMode})",
                securityMode,
                securityMode == OpcUaSecurityMode.None ? hel : opn,
                steps));

            var okMsg = $"已连接 {url}（安全模式：{securityMode}，策略：{policyUri[(policyUri.IndexOf('#') + 1)..]}）。";
            Log("Info", "ActivateSession", okMsg);
            return new OpcUaOperationResult(true, OpcUaStatusCodes.Good, okMsg);
        }

        public void Disconnect()
        {
            if (State == OpcUaConnectionState.Disconnected) return;
            State = OpcUaConnectionState.Disconnected;
            HandshakeRecorded?.Invoke(new OpcUaHandshakeTrace(
                DateTime.Now, "关闭 OPC UA 会话与安全通道", ActiveSecurityMode, [],
                [
                    new OpcUaHandshakeStep("CloseSession", true, "DeleteSubscriptions=true"),
                    new OpcUaHandshakeStep("CLO", true, "CloseSecureChannel"),
                ]));
            Log("Info", "CloseSession", "已安全关闭 OPC UA Session 与 SecureChannel。");
        }

        public OpcUaOperationResult ReadNode(string nodeId)
        {
            if (State != OpcUaConnectionState.Connected)
            {
                return Fail(OpcUaStatusCodes.BadSessionClosed, "Read", "当前未建立 OPC UA 会话 (BadSessionClosed)。");
            }

            if (!_nodes.TryGetValue(nodeId, out var node))
            {
                return Fail(OpcUaStatusCodes.BadNodeIdUnknown, "Read", $"未找到节点 {nodeId} (BadNodeIdUnknown)。");
            }

            node.ServerTimestamp = DateTime.Now;
            var msg = $"Read [{node.DisplayName}] ({node.NodeId}) = {node.ValueText} {node.EngineeringUnit} · {OpcUaStatusCodes.Format(node.StatusCode)}".Trim();
            Log(OpcUaStatusCodes.IsGood(node.StatusCode) ? "Info" : "Warn", "Read", msg);
            return new OpcUaOperationResult(OpcUaStatusCodes.IsGood(node.StatusCode), node.StatusCode, msg, node.ValueText);
        }

        public OpcUaOperationResult WriteNode(string nodeId, string newValueText)
        {
            if (State != OpcUaConnectionState.Connected)
            {
                return Fail(OpcUaStatusCodes.BadSessionClosed, "Write", "当前未建立 OPC UA 会话 (BadSessionClosed)。");
            }

            if (!_nodes.TryGetValue(nodeId, out var node))
            {
                return Fail(OpcUaStatusCodes.BadNodeIdUnknown, "Write", $"未找到节点 {nodeId} (BadNodeIdUnknown)。");
            }

            if (node.NodeClass != OpcUaNodeClass.Variable || !node.IsWritable)
            {
                return Fail(OpcUaStatusCodes.BadNotWritable, "Write",
                    $"拒绝写入 [{node.DisplayName}] ({node.NodeId})：节点为只读 (BadNotWritable 0x803B0000)。");
            }

            var raw = (newValueText ?? string.Empty).Trim();
            switch (node.DataType)
            {
                case "Double":
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var dVal) || !double.IsFinite(dVal))
                    {
                        return Fail(OpcUaStatusCodes.BadTypeMismatch, "Write",
                            $"类型不匹配：{node.DisplayName} 期望 Double 浮点数 (BadTypeMismatch 0x80740000)。");
                    }

                    dVal = Math.Clamp(dVal, 20.0, 320.0);
                    node.ValueText = dVal.ToString("F1", CultureInfo.InvariantCulture);
                    if (node.NodeId == TargetTempNodeId) _targetTemp = dVal;
                    break;

                case "Boolean":
                    if (!bool.TryParse(raw, out var bVal))
                    {
                        return Fail(OpcUaStatusCodes.BadTypeMismatch, "Write",
                            $"类型不匹配：{node.DisplayName} 期望 Boolean (true / false) (BadTypeMismatch 0x80740000)。");
                    }

                    node.ValueText = bVal ? "true" : "false";
                    break;

                default:
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return Fail(OpcUaStatusCodes.BadTypeMismatch, "Write", "字符串写入值不能为空 (BadTypeMismatch)。");
                    }

                    node.ValueText = raw;
                    break;
            }

            var now = DateTime.Now;
            node.SourceTimestamp = now;
            node.ServerTimestamp = now;
            node.StatusCode = OpcUaStatusCodes.Good;

            var msg = $"Write [{node.DisplayName}] ({node.NodeId}) ← {node.ValueText} · Good (0x00000000)。";
            Log("Info", "Write", msg);
            return new OpcUaOperationResult(true, OpcUaStatusCodes.Good, msg, node.ValueText);
        }

        public OpcUaOperationResult CallMethod(string methodNodeId)
        {
            if (State != OpcUaConnectionState.Connected)
            {
                return Fail(OpcUaStatusCodes.BadSessionClosed, "Call", "当前未建立 OPC UA 会话 (BadSessionClosed)。");
            }

            var now = DateTime.Now;
            if (methodNodeId == ResetAlarmMethodId)
            {
                IsSensorFaultInjected = false;
                var alarmNode = _nodes[AlarmActiveNodeId];
                alarmNode.ValueText = "false";
                alarmNode.SourceTimestamp = now;
                alarmNode.ServerTimestamp = now;

                var tempNode = _nodes[TempNodeId];
                tempNode.StatusCode = OpcUaStatusCodes.Good;
                tempNode.SourceTimestamp = now;
                tempNode.ServerTimestamp = now;

                var msg = "Call Method [ResetAlarm()] → Output: ResetOk=true · 已恢复热电偶状态码为 Good (0x00000000)。";
                Log("Info", "Call", msg);
                return new OpcUaOperationResult(true, OpcUaStatusCodes.Good, msg, "ResetOk=true");
            }

            if (methodNodeId == TriggerInspectMethodId)
            {
                _inspectTotal++;
                var isNg = _inspectTotal % 17 == 0;
                if (isNg) _inspectNg++;
                var yieldPct = (_inspectTotal - _inspectNg) / (double)_inspectTotal * 100.0;

                _nodes[NgCountNodeId].ValueText = _inspectNg.ToString(CultureInfo.InvariantCulture);
                _nodes[NgCountNodeId].SourceTimestamp = now;
                _nodes[NgCountNodeId].ServerTimestamp = now;

                _nodes[YieldNodeId].ValueText = yieldPct.ToString("F2", CultureInfo.InvariantCulture);
                _nodes[YieldNodeId].SourceTimestamp = now;
                _nodes[YieldNodeId].ServerTimestamp = now;

                var outText = $"Result={(isNg ? "NG" : "OK")}, Total={_inspectTotal}, Yield={yieldPct:F2}%";
                var msg = $"Call Method [TriggerInspect()] → {outText}";
                Log("Info", "Call", msg);
                return new OpcUaOperationResult(true, OpcUaStatusCodes.Good, msg, outText);
            }

            return Fail(OpcUaStatusCodes.BadNodeIdUnknown, "Call", $"未知方法节点 {methodNodeId}。");
        }

        public void SetSensorFault(bool faultActive)
        {
            IsSensorFaultInjected = faultActive;
            var now = DateTime.Now;
            var tempNode = _nodes[TempNodeId];
            tempNode.StatusCode = faultActive ? OpcUaStatusCodes.BadSensorFailure : OpcUaStatusCodes.Good;
            tempNode.SourceTimestamp = now;
            tempNode.ServerTimestamp = now;

            var alarmNode = _nodes[AlarmActiveNodeId];
            alarmNode.ValueText = faultActive ? "true" : "false";
            alarmNode.SourceTimestamp = now;
            alarmNode.ServerTimestamp = now;

            Log(faultActive ? "Error" : "Info", "Fault",
                faultActive
                    ? "注入热电偶断线故障：Zone1Temp StatusCode 变为 BadSensorFailure (0x808F0000)，订阅将立刻推送质量码变更！"
                    : "已清除热电偶故障：Zone1Temp StatusCode 恢复 Good (0x00000000)。");
        }

        public void ResetSubscriptionStats()
        {
            TotalSamplesCount = 0;
            FilteredByDeadbandCount = 0;
            PublishedNotificationsCount = 0;
            KeepAliveCount = 0;
            _silentPublishCycles = 0;
            _hasPendingPublishChange = false;
        }

        /// <summary>
        /// Advances one Server sampling tick (SamplingIntervalMs) on Zone1Temp, evaluating Deadband filter
        /// and emitting a batched DataChangeNotification or KeepAlive every PublishingIntervalMs.
        /// </summary>
        public OpcUaMonitoredSample StepSubscriptionSample(bool injectProcessStep = false)
        {
            _sampleIndex++;
            TotalSamplesCount++;
            var now = DateTime.Now;

            // Simulate realistic industrial oven temperature: gentle approach to TargetTemp + small oscillation + sensor jitter
            if (injectProcessStep)
            {
                _currentTemp += 3.2;
            }
            else
            {
                var diff = _targetTemp - _currentTemp;
                var drift = diff * 0.14 + Math.Sin(_sampleIndex * 0.22) * 0.42 + (_rng.NextDouble() - 0.5) * 0.36;
                // Periodic realistic process disturbance every ~18 samples
                if (_sampleIndex % 18 == 0)
                {
                    drift += 1.9;
                }

                _currentTemp = Math.Round(Math.Clamp(_currentTemp + drift, 20.0, 320.0), 2);
            }

            var status = IsSensorFaultInjected ? OpcUaStatusCodes.BadSensorFailure : OpcUaStatusCodes.Good;
            var tempNode = _nodes[TempNodeId];
            tempNode.ValueText = _currentTemp.ToString("F2", CultureInfo.InvariantCulture);
            tempNode.StatusCode = status;
            tempNode.SourceTimestamp = now;
            tempNode.ServerTimestamp = now;

            // Evaluate Absolute Deadband filter (StatusCode change ALWAYS bypasses Deadband per OPC UA Part 4 §7.17!)
            var statusChanged = status != _lastReportedStatus;
            var valueDelta = Math.Abs(_currentTemp - _lastReportedTemp);
            var passedDeadband = statusChanged || DeadbandAbsolute <= 1e-6 || valueDelta > DeadbandAbsolute;

            if (passedDeadband)
            {
                _hasPendingPublishChange = true;
                _pendingPublishTemp = _currentTemp;
                _pendingPublishStatus = status;
            }
            else
            {
                FilteredByDeadbandCount++;
            }

            // Determine whether current sample coincides with a PublishingInterval tick
            var samplesPerPublish = Math.Max(1, (int)Math.Round(PublishingIntervalMs / (double)Math.Max(20, SamplingIntervalMs)));
            var isPublishTick = _sampleIndex % samplesPerPublish == 0;
            var publishedNow = false;
            var keepAliveNow = false;

            if (State == OpcUaConnectionState.Connected && isPublishTick)
            {
                if (_hasPendingPublishChange)
                {
                    _hasPendingPublishChange = false;
                    _silentPublishCycles = 0;
                    _lastReportedTemp = _pendingPublishTemp;
                    _lastReportedStatus = _pendingPublishStatus;
                    publishedNow = true;
                    PublishedNotificationsCount++;

                    var detail = statusChanged
                        ? $"质量码突变触发推送 ({OpcUaStatusCodes.Format(_pendingPublishStatus)})"
                        : $"Δ={valueDelta:F2} °C > 死区 ±{DeadbandAbsolute:F2} °C → 推送 DataChange";

                    NotificationPublished?.Invoke(new OpcUaNotificationRecord(
                        _notificationSeq++, now, "DataChange", TempNodeId,
                        $"{_pendingPublishTemp:F2} °C", _pendingPublishStatus, detail));
                }
                else
                {
                    _silentPublishCycles++;
                    if (_silentPublishCycles >= MaxKeepAliveCount)
                    {
                        _silentPublishCycles = 0;
                        keepAliveNow = true;
                        KeepAliveCount++;
                        NotificationPublished?.Invoke(new OpcUaNotificationRecord(
                            _notificationSeq++, now, "KeepAlive", "Subscription #1",
                            "—", OpcUaStatusCodes.Good, $"连续 {MaxKeepAliveCount} 个发布周期无有效变更，发送 KeepAlive 保活心跳"));
                    }
                }
            }

            return new OpcUaMonitoredSample(
                _sampleIndex, now, _currentTemp, _lastReportedTemp, DeadbandAbsolute,
                passedDeadband, publishedNow, keepAliveNow, status);
        }

        private void BuildAddressSpace()
        {
            var now = DateTime.Now;
            AddNode("ns=0;i=85", "Objects", "Objects (根对象文件夹)", "", OpcUaNodeClass.Object, "FolderType", false, "", "", "OPC UA 标准命名空间 0 对象根节点", now);
            AddNode("ns=2;s=SmartFactory", "SmartFactory", "SmartFactory (数字工厂)", "ns=0;i=85", OpcUaNodeClass.Object, "FolderType", false, "", "", "工厂信息模型根节点", now);
            AddNode("ns=2;s=Line1.Oven", "ReflowOven", "ReflowOven (回流焊设备)", "ns=2;s=SmartFactory", OpcUaNodeClass.Object, "DeviceType", false, "", "", "一号线回流焊温区设备对象", now);
            AddNode(TempNodeId, "Zone1Temp", "Zone1Temp (一区实测炉温)", "ns=2;s=Line1.Oven", OpcUaNodeClass.Variable, "Double", false, "°C", "185.00", "热电偶实时反馈温度（只读变量）", now);
            AddNode(TargetTempNodeId, "TargetTemp", "TargetTemp (设定目标炉温)", "ns=2;s=Line1.Oven", OpcUaNodeClass.Variable, "Double", true, "°C", "185.0", "工艺目标温度设定值（可读写 20~320°C）", now);
            AddNode(OvenRunningNodeId, "Running", "Running (运行状态)", "ns=2;s=Line1.Oven", OpcUaNodeClass.Variable, "Boolean", true, "", "true", "设备启停布尔控制位（true/false）", now);
            AddNode(RecipeNodeId, "RecipeName", "RecipeName (当前配方号)", "ns=2;s=Line1.Oven", OpcUaNodeClass.Variable, "String", true, "", "PCB-LEADFREE-245", "当前绑定生产配方名称", now);
            AddNode(AlarmActiveNodeId, "AlarmActive", "AlarmActive (报警标志)", "ns=2;s=Line1.Oven", OpcUaNodeClass.Variable, "Boolean", false, "", "false", "设备综合报警状态位（只读）", now);
            AddNode(ResetAlarmMethodId, "ResetAlarm", "ResetAlarm() 复位报警", "ns=2;s=Line1.Oven", OpcUaNodeClass.Method, "Method", false, "", "RPC", "远程调用方法：复位设备报警与传感器故障状态", now);

            AddNode("ns=2;s=Line1.Vision", "VisionStation", "VisionStation (AOI 视觉站)", "ns=2;s=SmartFactory", OpcUaNodeClass.Object, "DeviceType", false, "", "", "一号线 AOI 视觉检测工位对象", now);
            AddNode(YieldNodeId, "YieldRate", "YieldRate (良率)", "ns=2;s=Line1.Vision", OpcUaNodeClass.Variable, "Double", false, "%", "99.67", "视觉工位实时良率统计（只读）", now);
            AddNode(NgCountNodeId, "NgCount", "NgCount (不良品计数)", "ns=2;s=Line1.Vision", OpcUaNodeClass.Variable, "Int32", false, "pcs", "4", "视觉工位累计 NG 计数（只读）", now);
            AddNode(TriggerInspectMethodId, "TriggerInspect", "TriggerInspect() 触发检测", "ns=2;s=Line1.Vision", OpcUaNodeClass.Method, "Method", false, "", "RPC", "远程调用方法：触发一次视觉拍照并返回 OK/NG", now);
        }

        private void AddNode(
            string nodeId, string browseName, string displayName, string parentId,
            OpcUaNodeClass nodeClass, string dataType, bool writable, string unit, string value, string desc, DateTime now)
        {
            var rec = new NodeRecord
            {
                NodeId = nodeId,
                BrowseName = browseName,
                DisplayName = displayName,
                ParentNodeId = parentId,
                NodeClass = nodeClass,
                DataType = dataType,
                IsWritable = writable,
                EngineeringUnit = unit,
                ValueText = value,
                Description = desc,
                SourceTimestamp = now,
                ServerTimestamp = now,
            };
            _nodes[nodeId] = rec;
            _orderedNodeIds.Add(nodeId);
        }

        private OpcUaOperationResult Fail(uint code, string service, string msg)
        {
            Log("Error", service, msg);
            return new OpcUaOperationResult(false, code, msg);
        }

        private static OpcUaNodeSnapshot BuildSnapshot(NodeRecord n) => new(
            n.NodeId, n.BrowseName, n.DisplayName, n.ParentNodeId, n.NodeClass,
            n.DataType, n.IsWritable, n.EngineeringUnit, n.ValueText,
            n.StatusCode, n.SourceTimestamp, n.ServerTimestamp, n.Description);

        private void Log(string level, string service, string message) =>
            Logged?.Invoke(new OpcUaLogEntry(DateTime.Now, level, service, message));
    }
}
