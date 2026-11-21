using LearnSmartManufacturing.Models.Mqtt;

namespace LearnSmartManufacturing.Services.Mqtt
{
    /// <summary>
    /// Full-fidelity in-memory MQTT 3.1.1 broker simulator supporting wildcard routing (+/#),
    /// QoS 0/1/2 handshake traces (with packet-loss &amp; DUP retransmission simulation),
    /// Retained messages, Last Will &amp; Testament (LWT), and edge Store-and-Forward replay.
    /// </summary>
    public sealed class InMemoryMqttBroker
    {
        private const int MaxOfflineQueue = 64;

        private readonly Dictionary<string, MqttSubscriptionEntry> _subscriptions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MqttRetainedEntry> _retained = new(StringComparer.Ordinal);
        private readonly Queue<(string Topic, string Payload, MqttQos Qos, bool Retain)> _offlineBuffer = new();

        private MqttWillConfig? _registeredWill;
        private ushort _nextPacketId = 1;

        public MqttConnectionState State { get; private set; } = MqttConnectionState.Disconnected;
        public string ClientId { get; private set; } = "edge-line1-gw";
        public int OfflineBufferedCount => _offlineBuffer.Count;
        public int ReplayedCount { get; private set; }
        public long PublishedCount { get; private set; }

        public event Action<MqttHandshakeTrace>? HandshakeRecorded;
        public event Action<MqttReceivedMessage>? MessageDelivered;
        public event Action<MqttProtocolLog>? ProtocolLogged;

        public IReadOnlyList<MqttSubscriptionEntry> GetSubscriptions() => [.. _subscriptions.Values];

        public IReadOnlyList<MqttRetainedEntry> GetRetainedMessages() => [.. _retained.Values.OrderBy(r => r.Topic, StringComparer.Ordinal)];

        public void Connect(string clientId, MqttWillConfig? will)
        {
            ClientId = string.IsNullOrWhiteSpace(clientId) ? "edge-line1-gw" : clientId.Trim();
            _registeredWill = will;
            State = MqttConnectionState.Connected;

            var connectPacket = MqttPacketCodec.BuildConnectPacket(ClientId, keepAliveSec: 30, will);
            var steps = new List<MqttHandshakeStep>
            {
                new("CONNECT", true, will is not null ? $"ClientId={ClientId}, LWT={will.Topic}" : $"ClientId={ClientId}"),
                new("CONNACK", false, "ReturnCode=0x00 (Connection Accepted)"),
            };
            HandshakeRecorded?.Invoke(new MqttHandshakeTrace(DateTime.Now, "MQTT 3.1.1 建连握手", MqttQos.AtMostOnce, connectPacket, steps));
            Log("Info", "CONNACK", $"客户端 {ClientId} 已连接至模拟 Broker（已注册 LWT 遗嘱：{will?.Topic ?? "无"}，报文 {connectPacket.Length} B）。");

            // Replay Store-and-Forward offline queue upon reconnection.
            if (_offlineBuffer.Count > 0)
            {
                var replayTotal = _offlineBuffer.Count;
                Log("Info", "REPLAY", $"检测到离线缓存 {replayTotal} 条 QoS≥1 消息，正在按序补发至 Broker...");
                while (_offlineBuffer.Count > 0)
                {
                    var item = _offlineBuffer.Dequeue();
                    ReplayedCount++;
                    PublishInternal(item.Topic, item.Payload, item.Qos, item.Retain, simulateDrop: false, isReplayed: true);
                }
            }
        }

        public void DisconnectGracefully()
        {
            if (State == MqttConnectionState.Disconnected)
            {
                return;
            }

            State = MqttConnectionState.Disconnected;
            _registeredWill = null; // Graceful DISCONNECT suppresses LWT per MQTT 3.1.1 §3.1.2.5.

            var discPacket = MqttPacketCodec.BuildDisconnectPacket();
            HandshakeRecorded?.Invoke(new MqttHandshakeTrace(DateTime.Now, "正常断开 (不触发 LWT 遗嘱)", MqttQos.AtMostOnce, discPacket,
            [
                new MqttHandshakeStep("DISCONNECT", true, "0xE0 0x00 · Broker 销毁已注册 LWT"),
            ]));
            Log("Info", "DISCONNECT", "已发送正常 DISCONNECT 控制包，Broker 自动丢弃 LWT 遗嘱。");
        }

        /// <summary>Simulates abrupt cable unplug / KeepAlive timeout, causing Broker to publish LWT.</summary>
        public void SimulateUnexpectedDisconnect()
        {
            if (State != MqttConnectionState.Connected)
            {
                return;
            }

            State = MqttConnectionState.NetworkLost;
            var will = _registeredWill;
            var steps = new List<MqttHandshakeStep>
            {
                new("TCP RST / 1.5×KeepAlive", true, "链路异常中断（未收到 DISCONNECT）", IsLost: true),
            };

            byte[] raw = [];
            if (will is not null)
            {
                raw = MqttPacketCodec.BuildPublishPacket(will.Topic, will.Payload, will.Qos, will.Retain, packetId: NextPacketId());
                steps.Add(new MqttHandshakeStep("LWT PUBLISH", false, $"Broker 代发遗嘱 → {will.Topic}", IsLwt: true));
                StoreRetainedIfNeeded(will.Topic, will.Payload, will.Qos, will.Retain);
                RouteToSubscribers(will.Topic, will.Payload, will.Qos, isRetainedDelivery: false, isLwt: true, isDup: false, isReplayed: false);
                Log("Error", "LWT", $"异常断网！Broker 触发遗嘱消息 → {will.Topic} ({will.Payload})，后续 QoS≥1 发布将自动进入本地离线缓存。");
            }
            else
            {
                Log("Warn", "NET-DROP", "异常断网（未配置 LWT 遗嘱）。");
            }

            HandshakeRecorded?.Invoke(new MqttHandshakeTrace(DateTime.Now, "异常掉线 · Broker 自动触发 LWT 遗嘱", will?.Qos ?? MqttQos.AtLeastOnce, raw, steps));
        }

        public bool Subscribe(string filter, MqttQos maxQos, out string error)
        {
            if (!MqttTopicMatcher.IsValidTopicFilter(filter, out error))
            {
                return false;
            }

            if (!_subscriptions.ContainsKey(filter))
            {
                _subscriptions[filter] = new MqttSubscriptionEntry(filter, maxQos, 0);
            }

            var pid = NextPacketId();
            var subPacket = MqttPacketCodec.BuildSubscribePacket(pid, filter, maxQos);
            HandshakeRecorded?.Invoke(new MqttHandshakeTrace(DateTime.Now, $"订阅主题 {filter}", maxQos, subPacket,
            [
                new MqttHandshakeStep("SUBSCRIBE", true, $"PacketId={pid}, Filter={filter}, QoS={(int)maxQos}"),
                new MqttHandshakeStep("SUBACK", false, $"PacketId={pid}, Granted QoS {(int)maxQos}"),
            ]));
            Log("Info", "SUBACK", $"已订阅过滤器 \"{filter}\" (最大 QoS {(int)maxQos})。");

            // Deliver any matching Retained messages immediately per MQTT 3.1.1 §3.8.4.
            var deliveredRetained = 0;
            foreach (var entry in _retained.Values)
            {
                if (MqttTopicMatcher.IsMatchWithParentMultiLevel(filter, entry.Topic))
                {
                    deliveredRetained++;
                    var effectiveQos = (MqttQos)Math.Min((byte)maxQos, (byte)entry.Qos);
                    IncrementHit(filter);
                    MessageDelivered?.Invoke(new MqttReceivedMessage(
                        DateTime.Now, entry.Topic, entry.Payload, effectiveQos,
                        IsRetained: true, IsLwt: false, IsDuplicate: false, IsReplayed: false, MatchedFilter: filter));
                }
            }

            if (deliveredRetained > 0)
            {
                Log("Info", "RETAIN", $"新订阅 \"{filter}\" 立即收到 {deliveredRetained} 条 Broker 保留消息快照。");
            }

            return true;
        }

        public void Unsubscribe(string filter)
        {
            if (_subscriptions.Remove(filter))
            {
                Log("Info", "UNSUBACK", $"已取消订阅过滤器 \"{filter}\"。");
            }
        }

        public bool Publish(string topic, string payload, MqttQos qos, bool retain, bool simulatePacketDrop, out string error)
        {
            if (!MqttTopicMatcher.IsValidTopicName(topic, out error))
            {
                return false;
            }

            if (State == MqttConnectionState.Disconnected)
            {
                error = "当前未连接 Broker，请先连接。";
                return false;
            }

            if (State == MqttConnectionState.NetworkLost)
            {
                if (qos == MqttQos.AtMostOnce)
                {
                    Log("Warn", "DROP-Q0", $"断网状态发布 QoS 0 消息 ({topic})：按协议不缓存直接丢弃。");
                }
                else
                {
                    if (_offlineBuffer.Count >= MaxOfflineQueue)
                    {
                        _offlineBuffer.Dequeue();
                    }

                    _offlineBuffer.Enqueue((topic, payload, qos, retain));
                    Log("Warn", "BUFFER", $"断网缓存 (Store-and-Forward)：已暂存 QoS {(int)qos} 消息 → {topic}（待补发 {_offlineBuffer.Count} 条）。");
                }

                return true;
            }

            PublishInternal(topic, payload, qos, retain, simulatePacketDrop, isReplayed: false);
            return true;
        }

        public IReadOnlyList<MqttRetainedEntry> ProbeRetainedForNewSubscriber(string filter)
        {
            if (!MqttTopicMatcher.IsValidTopicFilter(filter, out _))
            {
                return [];
            }

            return [.. _retained.Values.Where(r => MqttTopicMatcher.IsMatchWithParentMultiLevel(filter, r.Topic))];
        }

        private void PublishInternal(string topic, string payload, MqttQos qos, bool retain, bool simulateDrop, bool isReplayed)
        {
            PublishedCount++;
            var pid = qos == MqttQos.AtMostOnce ? (ushort)0 : NextPacketId();
            var rawPacket = MqttPacketCodec.BuildPublishPacket(topic, payload, qos, retain, dup: false, packetId: pid);
            var steps = new List<MqttHandshakeStep>();

            switch (qos)
            {
                case MqttQos.AtMostOnce:
                    if (simulateDrop)
                    {
                        steps.Add(new MqttHandshakeStep("PUBLISH (QoS 0)", true, $"{topic} · 网络丢包，无 ACK 也不重传", IsLost: true));
                        HandshakeRecorded?.Invoke(new MqttHandshakeTrace(DateTime.Now, "QoS 0 发布（模拟丢包）", qos, rawPacket, steps));
                        Log("Warn", "PUBLISH", $"QoS 0 报文丢包：{topic}（最多一次语义，订阅端未收到）。");
                        return;
                    }

                    steps.Add(new MqttHandshakeStep("PUBLISH (QoS 0)", true, $"{topic} · {rawPacket.Length} B (发完即忘)"));
                    HandshakeRecorded?.Invoke(new MqttHandshakeTrace(DateTime.Now, "QoS 0 最多一次发布", qos, rawPacket, steps));
                    StoreRetainedIfNeeded(topic, payload, qos, retain);
                    RouteToSubscribers(topic, payload, qos, isRetainedDelivery: false, isLwt: false, isDup: false, isReplayed);
                    Log("Info", "PUBLISH", $"QoS 0 发布 {topic} ({rawPacket.Length} B{(retain ? ", RETAIN" : string.Empty)})。");
                    break;

                case MqttQos.AtLeastOnce:
                    steps.Add(new MqttHandshakeStep("PUBLISH (QoS 1)", true, $"Id={pid} · {topic}"));
                    if (simulateDrop)
                    {
                        steps.Add(new MqttHandshakeStep("PUBACK 丢失", false, $"Id={pid} · ACK 在网络中丢失", IsLost: true));
                        var dupPacket = MqttPacketCodec.BuildPublishPacket(topic, payload, qos, retain, dup: true, packetId: pid);
                        steps.Add(new MqttHandshakeStep("PUBLISH (DUP=1)", true, $"Id={pid} · 客户端超时重传 (可能重复消费)", IsRetransmit: true));
                        steps.Add(new MqttHandshakeStep("PUBACK", false, $"Id={pid} · 确认送达"));
                        HandshakeRecorded?.Invoke(new MqttHandshakeTrace(DateTime.Now, "QoS 1 至少一次（触发 DUP 重传）", qos, dupPacket, steps));
                        StoreRetainedIfNeeded(topic, payload, qos, retain);
                        RouteToSubscribers(topic, payload, qos, false, false, isDup: false, isReplayed);
                        RouteToSubscribers(topic, payload, qos, false, false, isDup: true, isReplayed);
                        Log("Warn", "PUBACK", $"QoS 1 (Id={pid}) PUBACK 丢失触发 DUP=1 重传：订阅端收到 2 次（演示业务层需做幂等去重）。");
                    }
                    else
                    {
                        steps.Add(new MqttHandshakeStep("PUBACK", false, $"Id={pid} · Broker 已落盘确认"));
                        HandshakeRecorded?.Invoke(new MqttHandshakeTrace(DateTime.Now, "QoS 1 两步握手确认", qos, rawPacket, steps));
                        StoreRetainedIfNeeded(topic, payload, qos, retain);
                        RouteToSubscribers(topic, payload, qos, false, false, isDup: false, isReplayed);
                        Log("Info", "PUBACK", $"QoS 1 发布 {topic} (Id={pid}, {rawPacket.Length} B) 已收到 PUBACK。");
                    }

                    break;

                case MqttQos.ExactlyOnce:
                    steps.Add(new MqttHandshakeStep("PUBLISH (QoS 2)", true, $"Id={pid} · {topic}"));
                    if (simulateDrop)
                    {
                        steps.Add(new MqttHandshakeStep("PUBREC 丢失", false, $"Id={pid} · 重传但 Broker 按 Id 去重", IsLost: true));
                        steps.Add(new MqttHandshakeStep("PUBLISH (DUP=1)", true, $"Id={pid} · 幂等去重不重复投递", IsRetransmit: true));
                    }

                    steps.Add(new MqttHandshakeStep("PUBREC", false, $"Id={pid} · Broker 已接收并锁定 Id"));
                    steps.Add(new MqttHandshakeStep("PUBREL", true, $"Id={pid} · 客户端释放消息载荷"));
                    steps.Add(new MqttHandshakeStep("PUBCOMP", false, $"Id={pid} · 四步握手完成（恰好一次）"));
                    HandshakeRecorded?.Invoke(new MqttHandshakeTrace(DateTime.Now, "QoS 2 四步握手（恰好一次）", qos, rawPacket, steps));
                    StoreRetainedIfNeeded(topic, payload, qos, retain);
                    RouteToSubscribers(topic, payload, qos, false, false, isDup: false, isReplayed);
                    Log("Info", "PUBCOMP", $"QoS 2 发布 {topic} (Id={pid}) 完成 PUBLISH→PUBREC→PUBREL→PUBCOMP 四步握手（严格不丢不重）。");
                    break;
            }
        }

        private void StoreRetainedIfNeeded(string topic, string payload, MqttQos qos, bool retain)
        {
            if (!retain)
            {
                return;
            }

            if (string.IsNullOrEmpty(payload))
            {
                if (_retained.Remove(topic))
                {
                    Log("Info", "RETAIN", $"收到空载荷 Retain 消息，已清除主题 {topic} 的保留快照。");
                }
            }
            else
            {
                _retained[topic] = new MqttRetainedEntry(topic, payload, qos, DateTime.Now);
            }
        }

        private void RouteToSubscribers(string topic, string payload, MqttQos pubQos, bool isRetainedDelivery, bool isLwt, bool isDup, bool isReplayed)
        {
            foreach (var filter in _subscriptions.Keys.ToArray())
            {
                if (MqttTopicMatcher.IsMatchWithParentMultiLevel(filter, topic))
                {
                    var sub = _subscriptions[filter];
                    var effectiveQos = (MqttQos)Math.Min((byte)sub.MaxQos, (byte)pubQos);
                    IncrementHit(filter);
                    MessageDelivered?.Invoke(new MqttReceivedMessage(
                        DateTime.Now, topic, payload, effectiveQos,
                        isRetainedDelivery, isLwt, isDup, isReplayed, filter));
                }
            }
        }

        private void IncrementHit(string filter)
        {
            if (_subscriptions.TryGetValue(filter, out var existing))
            {
                _subscriptions[filter] = existing with { HitCount = existing.HitCount + 1 };
            }
        }

        private ushort NextPacketId()
        {
            var id = _nextPacketId++;
            if (_nextPacketId == 0)
            {
                _nextPacketId = 1;
            }

            return id;
        }

        private void Log(string level, string packet, string message) =>
            ProtocolLogged?.Invoke(new MqttProtocolLog(DateTime.Now, level, packet, message));
    }
}
