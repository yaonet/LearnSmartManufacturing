using LearnSmartManufacturing.Models.Mqtt;
using LearnSmartManufacturing.Services.Mqtt;
using Xunit;

namespace LearnSmartManufacturing.Tests.Services
{
    public class InMemoryMqttBrokerTests
    {
        [Fact]
        public void Qos0_WithSimulatedDrop_DeliversZeroMessages()
        {
            var broker = new InMemoryMqttBroker();
            var received = new List<MqttReceivedMessage>();
            MqttHandshakeTrace? trace = null;
            broker.MessageDelivered += received.Add;
            broker.HandshakeRecorded += t => trace = t;

            broker.Connect("gw-1", null);
            broker.Subscribe("factory/line1/+/telemetry", MqttQos.AtMostOnce, out _);

            var ok = broker.Publish("factory/line1/smt-01/telemetry", "{\"t\":180}", MqttQos.AtMostOnce, retain: false, simulatePacketDrop: true, out _);

            Assert.True(ok);
            Assert.Empty(received);
            Assert.NotNull(trace);
            Assert.Contains(trace!.Steps, s => s.IsLost);
        }

        [Fact]
        public void Qos1_WithSimulatedDrop_TriggersDupRetransmitAndDeliversTwice()
        {
            var broker = new InMemoryMqttBroker();
            var received = new List<MqttReceivedMessage>();
            MqttHandshakeTrace? trace = null;
            broker.MessageDelivered += received.Add;
            broker.HandshakeRecorded += t => trace = t;

            broker.Connect("gw-1", null);
            broker.Subscribe("factory/line1/+/telemetry", MqttQos.AtLeastOnce, out _);

            broker.Publish("factory/line1/smt-01/telemetry", "{\"t\":185}", MqttQos.AtLeastOnce, retain: false, simulatePacketDrop: true, out _);

            Assert.Equal(2, received.Count);
            Assert.False(received[0].IsDuplicate);
            Assert.True(received[1].IsDuplicate);
            Assert.NotNull(trace);
            Assert.Contains(trace!.Steps, s => s.IsRetransmit);
        }

        [Fact]
        public void Qos2_WithSimulatedDrop_CompletesFourStepHandshakeAndDeliversExactlyOnce()
        {
            var broker = new InMemoryMqttBroker();
            var received = new List<MqttReceivedMessage>();
            MqttHandshakeTrace? trace = null;
            broker.MessageDelivered += received.Add;
            broker.HandshakeRecorded += t => trace = t;

            broker.Connect("gw-1", null);
            broker.Subscribe("factory/line1/+/alarm", MqttQos.ExactlyOnce, out _);

            broker.Publish("factory/line1/smt-01/alarm", "{\"code\":\"E402\"}", MqttQos.ExactlyOnce, retain: false, simulatePacketDrop: true, out _);

            Assert.Single(received);
            Assert.False(received[0].IsDuplicate);
            Assert.NotNull(trace);
            Assert.Contains(trace!.Steps, s => s.PacketType == "PUBREC");
            Assert.Contains(trace.Steps, s => s.PacketType == "PUBREL");
            Assert.Contains(trace.Steps, s => s.PacketType == "PUBCOMP");
        }

        [Fact]
        public void RetainedMessage_DeliversToLateSubscriber_AndClearsOnEmptyPayload()
        {
            var broker = new InMemoryMqttBroker();
            var received = new List<MqttReceivedMessage>();
            broker.MessageDelivered += received.Add;

            broker.Connect("gw-1", null);
            broker.Publish("factory/line1/smt-01/status", "{\"online\":true}", MqttQos.AtLeastOnce, retain: true, simulatePacketDrop: false, out _);
            Assert.Single(broker.GetRetainedMessages());

            // Late subscriber receives retained snapshot immediately
            broker.Subscribe("factory/line1/+/status", MqttQos.AtLeastOnce, out _);
            Assert.Single(received);
            Assert.True(received[0].IsRetained);

            // Empty payload + retain=true removes stored retained entry
            broker.Publish("factory/line1/smt-01/status", string.Empty, MqttQos.AtLeastOnce, retain: true, simulatePacketDrop: false, out _);
            Assert.Empty(broker.GetRetainedMessages());
        }

        [Fact]
        public void GracefulDisconnect_SuppressesLwt_WhileUnexpectedDisconnectTriggersLwt()
        {
            var will = new MqttWillConfig("factory/line1/smt-01/status", "{\"online\":false}", MqttQos.AtLeastOnce, Retain: true);

            // Case 1: Graceful disconnect does not publish LWT
            var broker1 = new InMemoryMqttBroker();
            var received1 = new List<MqttReceivedMessage>();
            broker1.MessageDelivered += received1.Add;
            broker1.Connect("gw-1", will);
            broker1.Subscribe("factory/line1/+/status", MqttQos.AtLeastOnce, out _);
            broker1.DisconnectGracefully();
            Assert.Empty(received1);

            // Case 2: Abrupt disconnect triggers LWT
            var broker2 = new InMemoryMqttBroker();
            var received2 = new List<MqttReceivedMessage>();
            broker2.MessageDelivered += received2.Add;
            broker2.Connect("gw-2", will);
            broker2.Subscribe("factory/line1/+/status", MqttQos.AtLeastOnce, out _);
            broker2.SimulateUnexpectedDisconnect();

            Assert.Single(received2);
            Assert.True(received2[0].IsLwt);
            Assert.Equal("{\"online\":false}", received2[0].Payload);
            Assert.Equal(MqttConnectionState.NetworkLost, broker2.State);
        }

        [Fact]
        public void OfflineStoreAndForward_BuffersQos1And2_DropsQos0_AndReplaysOnReconnect()
        {
            var broker = new InMemoryMqttBroker();
            var received = new List<MqttReceivedMessage>();
            broker.MessageDelivered += received.Add;

            broker.Connect("gw-1", null);
            broker.Subscribe("factory/line1/#", MqttQos.ExactlyOnce, out _);
            broker.SimulateUnexpectedDisconnect();

            broker.Publish("factory/line1/smt-01/telemetry", "drop-q0", MqttQos.AtMostOnce, false, false, out _);
            broker.Publish("factory/line1/smt-01/telemetry", "keep-q1", MqttQos.AtLeastOnce, false, false, out _);
            broker.Publish("factory/line1/smt-01/alarm", "keep-q2", MqttQos.ExactlyOnce, false, false, out _);

            Assert.Equal(2, broker.OfflineBufferedCount);
            Assert.Empty(received);

            broker.Connect("gw-1", null);

            Assert.Equal(0, broker.OfflineBufferedCount);
            Assert.Equal(2, broker.ReplayedCount);
            Assert.Equal(2, received.Count);
            Assert.All(received, m => Assert.True(m.IsReplayed));
            Assert.Equal("keep-q1", received[0].Payload);
            Assert.Equal("keep-q2", received[1].Payload);
        }

        [Fact]
        public void EffectiveQos_IsDowngradedToSubscriberMaxQos()
        {
            var broker = new InMemoryMqttBroker();
            var received = new List<MqttReceivedMessage>();
            broker.MessageDelivered += received.Add;

            broker.Connect("gw-1", null);
            broker.Subscribe("factory/line1/+/alarm", MqttQos.AtLeastOnce, out _);

            broker.Publish("factory/line1/smt-01/alarm", "{\"code\":\"E500\"}", MqttQos.ExactlyOnce, false, false, out _);

            Assert.Single(received);
            Assert.Equal(MqttQos.AtLeastOnce, received[0].Qos);
        }
    }
}
