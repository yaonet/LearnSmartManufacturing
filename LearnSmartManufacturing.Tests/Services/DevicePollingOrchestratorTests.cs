using LearnSmartManufacturing.Models.Async;
using LearnSmartManufacturing.Services.Async;

namespace LearnSmartManufacturing.Tests.Services
{
    public class DevicePollingOrchestratorTests
    {
        [Fact]
        public async Task RunBatchAsync_RespectsSemaphoreMaxConcurrency()
        {
            var orchestrator = new DevicePollingOrchestrator();
            var specs = Enumerable.Range(1, 6)
                .Select(i => new DeviceSpec(i, $"DEV-{i}", "Test", SimulatedDurationMs: 70))
                .ToArray();

            var result = await orchestrator.RunBatchAsync(specs, maxConcurrency: 2, deviceTimeoutMs: 1000);

            Assert.Equal(6, result.SucceededCount);
            Assert.Equal(0, result.TimedOutCount);
            Assert.Equal(0, result.FaultedCount);
            Assert.Equal(0, result.CanceledCount);
            Assert.InRange(result.PeakConcurrency, 1, 2);
        }

        [Fact]
        public async Task RunBatchAsync_IsolatesSingleDeviceTimeoutAndFault()
        {
            var orchestrator = new DevicePollingOrchestrator();
            DeviceSpec[] specs =
            [
                new(1, "DEV-OK-1", "PLC", 60),
                new(2, "DEV-TIMEOUT", "Sensor", 100, InjectTimeout: true),
                new(3, "DEV-FAULT", "Scanner", 100, InjectFault: true),
                new(4, "DEV-OK-2", "Vision", 60),
            ];

            var result = await orchestrator.RunBatchAsync(specs, maxConcurrency: 4, deviceTimeoutMs: 120);

            Assert.Equal(2, result.SucceededCount);
            Assert.Equal(1, result.TimedOutCount);
            Assert.Equal(1, result.FaultedCount);
            Assert.Equal(0, result.CanceledCount);
        }

        [Fact]
        public async Task RunBatchAsync_GlobalCancellation_CancelsAllRemainingDevices()
        {
            var orchestrator = new DevicePollingOrchestrator();
            var specs = Enumerable.Range(1, 6)
                .Select(i => new DeviceSpec(i, $"DEV-{i}", "PLC", SimulatedDurationMs: 500))
                .ToArray();

            using var cts = new CancellationTokenSource(60);
            var result = await orchestrator.RunBatchAsync(specs, maxConcurrency: 2, deviceTimeoutMs: 2000, cts.Token);

            Assert.True(result.CanceledCount > 0);
            Assert.Equal(0, result.SucceededCount);
            Assert.False(orchestrator.GetSnapshot().IsRunning);
        }
    }
}
