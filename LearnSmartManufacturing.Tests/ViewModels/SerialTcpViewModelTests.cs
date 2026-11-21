using LearnSmartManufacturing.Models.SerialTcp;
using LearnSmartManufacturing.ViewModels;
using Xunit;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class SerialTcpViewModelTests
    {
        [Fact]
        public void Constructor_InitializesWithInitialDecodedFrameAndRs485History()
        {
            using var vm = new SerialTcpViewModel();

            Assert.Equal(1, vm.ValidFrameCount);
            Assert.Single(vm.DecodedFrames);
            Assert.NotNull(vm.PipelineSnapshot);
            Assert.Equal(4, vm.Rs485BusEvents.Count);
            Assert.NotNull(vm.SerialTiming);
        }

        [Fact]
        public async Task StickyAndHalfPacketCommands_UpdateBufferAndDecodedCounts()
        {
            using var vm = new SerialTcpViewModel();
            vm.ClearFramingDataCommand.Execute(null);

            await vm.SendStickyBurstCommand.ExecuteAsync(null);
            Assert.Equal(3, vm.ValidFrameCount);
            Assert.Equal(0, vm.PendingBufferBytes);

            await vm.StartHalfPacketDemoCommand.ExecuteAsync(null);
            Assert.True(vm.PendingBufferBytes > 0);
            Assert.True(vm.HasPendingFragments);
            Assert.Equal(3, vm.ValidFrameCount); // Not complete yet

            await vm.DeliverNextFragmentCommand.ExecuteAsync(null);
            await vm.DeliverNextFragmentCommand.ExecuteAsync(null);

            Assert.Equal(0, vm.PendingBufferBytes);
            Assert.False(vm.HasPendingFragments);
            Assert.Equal(4, vm.ValidFrameCount);
        }

        [Fact]
        public async Task ChaosStressTest_DiscardsNoiseRejectsCorruptAndRecoversValidFrames()
        {
            using var vm = new SerialTcpViewModel();
            vm.ClearFramingDataCommand.Execute(null);

            await vm.SendChaosStressTestCommand.ExecuteAsync(null);

            Assert.Equal(2, vm.ValidFrameCount);
            Assert.Equal(1, vm.CrcErrorCount);
            Assert.Equal(4, vm.GarbageBytesDiscarded);
            Assert.Equal(0, vm.PendingBufferBytes);
        }

        [Fact]
        public void Rs485ConflictAndExponentialBackoffReconnect_TransitionStateCleanly()
        {
            using var vm = new SerialTcpViewModel();

            vm.SimulateConcurrentConflictCommand.Execute(null);
            Assert.Equal(1, vm.CollisionCount);
            Assert.False(vm.HalfDuplexLockEnabled);

            vm.SimulateLinkDropCommand.Execute(null);
            Assert.Equal(ReconnectLinkState.HeartbeatTimeout, vm.LinkState);

            vm.StepBackoffReconnectCommand.Execute(null); // Attempt 1 (1s)
            Assert.Equal(ReconnectLinkState.BackoffReconnecting, vm.LinkState);
            Assert.Equal(2, vm.NextBackoffSec);

            vm.StepBackoffReconnectCommand.Execute(null); // Attempt 2 (2s)
            Assert.Equal(4, vm.NextBackoffSec);

            vm.StepBackoffReconnectCommand.Execute(null); // Attempt 3 -> Recovered!
            Assert.Equal(ReconnectLinkState.Connected, vm.LinkState);
        }
    }
}
