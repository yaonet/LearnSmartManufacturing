using LearnSmartManufacturing.Models.Mqtt;
using LearnSmartManufacturing.ViewModels;
using Xunit;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class MqttViewModelTests
    {
        [Fact]
        public void Constructor_AutoConnectsSimulatedBrokerWithDefaultSubscriptionsAndRetainedState()
        {
            using var vm = new MqttViewModel();

            Assert.True(vm.IsSimulatedMode);
            Assert.True(vm.IsConnected);
            Assert.True(vm.IsDeviceOnline);
            Assert.Equal(3, vm.Subscriptions.Count);
            Assert.Equal(2, vm.RetainedEntries.Count);
            Assert.NotNull(vm.LatestTrace);
            Assert.True(vm.PublishedCount >= 2);
            Assert.True(vm.ReceivedCount >= 2);
        }

        [Fact]
        public async Task PublishCommand_DeliversMatchingMessageAndUpdatesMetrics()
        {
            using var vm = new MqttViewModel();
            var beforePub = vm.PublishedCount;
            var beforeRecv = vm.ReceivedCount;

            vm.PublishTopic = "factory/line1/smt-01/telemetry";
            vm.PublishPayload = "{\"tempC\":191.5}";
            vm.SelectedQos = vm.QosOptions[1]; // QoS 1
            vm.SimulatePacketDrop = false;

            await vm.PublishCommand.ExecuteAsync(null);

            Assert.Equal(beforePub + 1, vm.PublishedCount);
            Assert.Equal(beforeRecv + 1, vm.ReceivedCount);
            Assert.Equal("{\"tempC\":191.5}", vm.ReceivedMessages[0].Payload);
        }

        [Fact]
        public async Task NetworkDrop_TriggersLwtBuffersOfflineMessagesAndReplaysOnReconnect()
        {
            using var vm = new MqttViewModel();

            vm.SimulateNetworkDropCommand.Execute(null);

            Assert.Equal(MqttConnectionState.NetworkLost, vm.ConnectionState);
            Assert.False(vm.IsDeviceOnline);
            Assert.True(vm.ReceivedMessages[0].IsLwt);

            // Publish a QoS 1 message during network outage -> buffered
            vm.PublishTopic = "factory/line1/smt-01/telemetry";
            vm.PublishPayload = "{\"offline\":1}";
            vm.SelectedQos = vm.QosOptions[1];
            await vm.PublishCommand.ExecuteAsync(null);
            Assert.Equal(1, vm.OfflineBufferedCount);

            // Reconnect -> replays buffer and restores online birth status
            vm.ReconnectNetworkCommand.Execute(null);

            Assert.Equal(MqttConnectionState.Connected, vm.ConnectionState);
            Assert.Equal(0, vm.OfflineBufferedCount);
            Assert.Equal(1, vm.ReplayedCount);
            Assert.True(vm.IsDeviceOnline);
            Assert.Contains(vm.ReceivedMessages, m => m.IsReplayed && m.Payload == "{\"offline\":1}");
        }

        [Fact]
        public void WildcardLab_EvaluatesFilterLiveAndDetectsInvalidSyntax()
        {
            using var vm = new MqttViewModel();

            vm.TestFilterText = "#";
            Assert.True(vm.IsTestFilterValid);
            var sysRow = vm.TopicMatchRows.Single(r => r.SampleTopic.StartsWith("$SYS"));
            Assert.False(sysRow.IsMatch);

            vm.TestFilterText = "$SYS/#";
            Assert.True(vm.IsTestFilterValid);
            sysRow = vm.TopicMatchRows.Single(r => r.SampleTopic.StartsWith("$SYS"));
            Assert.True(sysRow.IsMatch);

            vm.TestFilterText = "factory/#/invalid";
            Assert.False(vm.IsTestFilterValid);
            Assert.All(vm.TopicMatchRows, r => Assert.False(r.IsMatch));
        }

        [Fact]
        public void ClearRetainedCommand_RemovesTargetRetainedTopic()
        {
            using var vm = new MqttViewModel();
            var initialCount = vm.RetainedCount;
            var targetTopic = vm.RetainedEntries[0].Topic;

            vm.ClearRetainedCommand.Execute(targetTopic);

            Assert.Equal(initialCount - 1, vm.RetainedCount);
            Assert.DoesNotContain(vm.RetainedEntries, r => r.Topic == targetTopic);
        }
    }
}
