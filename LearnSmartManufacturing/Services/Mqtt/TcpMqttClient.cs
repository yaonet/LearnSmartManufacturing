using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using LearnSmartManufacturing.Models.Mqtt;

namespace LearnSmartManufacturing.Services.Mqtt
{
    /// <summary>
    /// Minimal real MQTT 3.1.1 TCP client capable of connecting to standard brokers (Mosquitto / EMQX on port 1883).
    /// </summary>
    public sealed class TcpMqttClient : IDisposable
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private TcpClient? _tcp;
        private CancellationTokenSource? _loopCts;
        private Task? _readLoopTask;
        private ushort _nextPacketId = 1;
        private bool _disposed;

        public bool IsConnected => _tcp is { Connected: true };

        public event Action<MqttReceivedMessage>? MessageReceived;
        public event Action<MqttProtocolLog>? ProtocolLogged;
        public event Action<string>? Disconnected;

        public async Task ConnectAsync(string host, int port, string clientId, MqttWillConfig? will, CancellationToken ct = default)
        {
            Disconnect();
            var tcp = new TcpClient { NoDelay = true };
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(3));

            try
            {
                await tcp.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
                var stream = tcp.GetStream();

                var connectPacket = MqttPacketCodec.BuildConnectPacket(clientId, keepAliveSec: 30, will);
                await stream.WriteAsync(connectPacket, connectCts.Token).ConfigureAwait(false);

                var resp = await MqttPacketCodec.ReadPacketAsync(stream, connectCts.Token).ConfigureAwait(false);
                if (resp is not { Header: 0x20, Body.Length: >= 2 } || resp.Value.Body[1] != 0x00)
                {
                    var code = resp is { Body.Length: >= 2 } ? resp.Value.Body[1] : (byte)0xFF;
                    throw new IOException($"MQTT CONNACK 拒绝连接 (ReturnCode=0x{code:X2})。");
                }

                _tcp = tcp;
                _loopCts = new CancellationTokenSource();
                _readLoopTask = Task.Run(() => RunReadLoopAsync(stream, _loopCts.Token), CancellationToken.None);
                Log("Info", "CONNACK", $"已成功连接真实 MQTT 3.1.1 Broker {host}:{port} (ClientId={clientId})。");
            }
            catch
            {
                tcp.Dispose();
                throw;
            }
        }

        public async Task SubscribeAsync(string filter, MqttQos qos, CancellationToken ct = default)
        {
            var pid = NextPacketId();
            var packet = MqttPacketCodec.BuildSubscribePacket(pid, filter, qos);
            await SendPacketAsync(packet, ct).ConfigureAwait(false);
            Log("Info", "SUBSCRIBE", $"发送 SUBSCRIBE \"{filter}\" (QoS {(int)qos}, PacketId={pid})。");
        }

        public async Task<byte[]> PublishAsync(string topic, string payload, MqttQos qos, bool retain, CancellationToken ct = default)
        {
            var pid = qos == MqttQos.AtMostOnce ? (ushort)0 : NextPacketId();
            var packet = MqttPacketCodec.BuildPublishPacket(topic, payload, qos, retain, dup: false, packetId: pid);
            await SendPacketAsync(packet, ct).ConfigureAwait(false);
            Log("Info", "PUBLISH", $"发送 PUBLISH {topic} (QoS {(int)qos}, {packet.Length} B)。");
            return packet;
        }

        public void Disconnect()
        {
            var tcp = _tcp;
            _tcp = null;

            if (tcp is { Connected: true })
            {
                try
                {
                    tcp.GetStream().Write(MqttPacketCodec.BuildDisconnectPacket());
                }
                catch
                {
                    // Best effort graceful disconnect.
                }
            }

            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _loopCts = null;
            tcp?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Disconnect();
            _writeLock.Dispose();
        }

        private async Task SendPacketAsync(byte[] packet, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_tcp is not { Connected: true })
                {
                    throw new InvalidOperationException("MQTT TCP 未连接。");
                }

                await _tcp.GetStream().WriteAsync(packet, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task RunReadLoopAsync(NetworkStream stream, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var packet = await MqttPacketCodec.ReadPacketAsync(stream, ct).ConfigureAwait(false);
                    if (packet is null)
                    {
                        break;
                    }

                    var (header, body) = packet.Value;
                    var packetType = (byte)(header >> 4);
                    switch (packetType)
                    {
                        case 3: // PUBLISH
                            if (MqttPacketCodec.TryParsePublishPacket(header, body, out var topic, out var payload, out var qos, out var retain, out var dup, out var pid))
                            {
                                if (qos == MqttQos.AtLeastOnce)
                                {
                                    await SendPacketAsync(MqttPacketCodec.BuildPubAckPacket(pid), ct).ConfigureAwait(false);
                                }
                                else if (qos == MqttQos.ExactlyOnce)
                                {
                                    await SendPacketAsync(MqttPacketCodec.BuildPubRecPacket(pid), ct).ConfigureAwait(false);
                                }

                                MessageReceived?.Invoke(new MqttReceivedMessage(
                                    DateTime.Now, topic, payload, qos, retain, IsLwt: false, dup, IsReplayed: false, MatchedFilter: "Broker"));
                            }

                            break;

                        case 4: // PUBACK
                            Log("Info", "PUBACK", $"收到 Broker PUBACK (Id={(body.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(body) : 0)})。");
                            break;

                        case 5: // PUBREC -> send PUBREL
                            if (body.Length >= 2)
                            {
                                var recId = BinaryPrimitives.ReadUInt16BigEndian(body);
                                await SendPacketAsync(MqttPacketCodec.BuildPubRelPacket(recId), ct).ConfigureAwait(false);
                                Log("Info", "PUBREC", $"收到 PUBREC (Id={recId})，已回复 PUBREL。");
                            }

                            break;

                        case 6: // PUBREL -> send PUBCOMP
                            if (body.Length >= 2)
                            {
                                var relId = BinaryPrimitives.ReadUInt16BigEndian(body);
                                await SendPacketAsync(MqttPacketCodec.BuildPubCompPacket(relId), ct).ConfigureAwait(false);
                            }

                            break;

                        case 7: // PUBCOMP
                            Log("Info", "PUBCOMP", $"收到 PUBCOMP (Id={(body.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(body) : 0)})，QoS 2 完成。");
                            break;

                        case 9: // SUBACK
                            Log("Info", "SUBACK", "收到 Broker SUBACK 订阅确认。");
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal disconnect.
            }
            catch (Exception ex)
            {
                Disconnected?.Invoke(ex.Message);
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
