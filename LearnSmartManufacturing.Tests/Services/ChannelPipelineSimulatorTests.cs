using System.Threading.Channels;
using LearnSmartManufacturing.Models.Async;
using LearnSmartManufacturing.Services.Async;

namespace LearnSmartManufacturing.Tests.Services
{
    public class ChannelPipelineSimulatorTests
    {
        [Fact]
        public async Task DropOldest_WhenOverloaded_DropsFramesAndRespectsCapacity()
        {
            using var sim = new ChannelPipelineSimulator();
            sim.Start(new PipelineConfig(
                ProduceRateHz: 200,
                ProcessTimeMs: 70,
                Capacity: 4,
                WorkerCount: 1,
                FullMode: BoundedChannelFullMode.DropOldest));

            await Task.Delay(260);
            var snap = sim.GetSnapshot();
            sim.Stop();

            Assert.True(snap.ProducedCount > 5);
            Assert.True(snap.DroppedCount > 0, $"Expected DropOldest to drop frames under overload, got {snap.DroppedCount}");
            Assert.InRange(snap.CurrentDepth, 0, 4);
            Assert.False(sim.IsRunning);
        }

        [Fact]
        public async Task WaitMode_WhenOverloaded_NeverDropsFrames()
        {
            using var sim = new ChannelPipelineSimulator();
            sim.Start(new PipelineConfig(
                ProduceRateHz: 200,
                ProcessTimeMs: 60,
                Capacity: 4,
                WorkerCount: 1,
                FullMode: BoundedChannelFullMode.Wait));

            await Task.Delay(260);
            var snap = sim.GetSnapshot();
            sim.Stop();

            Assert.True(snap.ProducedCount >= 4);
            Assert.Equal(0, snap.DroppedCount);
            Assert.InRange(snap.CurrentDepth, 0, 4);
        }

        [Fact]
        public async Task DropWrite_WhenOverloaded_DropsIncomingFrames()
        {
            using var sim = new ChannelPipelineSimulator();
            sim.Start(new PipelineConfig(
                ProduceRateHz: 200,
                ProcessTimeMs: 70,
                Capacity: 4,
                WorkerCount: 1,
                FullMode: BoundedChannelFullMode.DropWrite));

            await Task.Delay(260);
            var snap = sim.GetSnapshot();
            sim.Stop();

            Assert.True(snap.DroppedCount > 0);
            Assert.InRange(snap.CurrentDepth, 0, 4);
        }

        [Fact]
        public void Start_InvalidConfig_ThrowsArgumentOutOfRange()
        {
            using var sim = new ChannelPipelineSimulator();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                sim.Start(new PipelineConfig(0, 20, 4, 1, BoundedChannelFullMode.Wait)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                sim.Start(new PipelineConfig(50, 20, 0, 1, BoundedChannelFullMode.Wait)));
        }
    }
}
