using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class AsyncProgrammingViewModelTests
    {
        [Fact]
        public void ConfigureAwaitFixCommand_CompletesEvenUnderDeadlockingSyncContext()
        {
            var previous = SynchronizationContext.Current;
            // A SynchronizationContext whose Post never drains while the caller thread is blocked (simulates WPF Dispatcher during .Wait()).
            SynchronizationContext.SetSynchronizationContext(new NeverDrainingSynchronizationContext());
            try
            {
                using var vm = new AsyncProgrammingViewModel();
                vm.RunConfigureAwaitFixCommand.Execute(null);

                Assert.Contains("未死锁", vm.ResponsivenessStatusText);
                Assert.NotEmpty(vm.Tab1Logs);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [Fact]
        public async Task StartPipelineAndDispose_CleansUpBackgroundWorkers()
        {
            var vm = new AsyncProgrammingViewModel();
            vm.StartPipelineCommand.Execute(null);
            Assert.True(vm.IsPipelineRunning);

            await Task.Delay(80);
            vm.RefreshSnapshots();
            Assert.True(vm.PipelineSnapshot.ProducedCount > 0);

            vm.Dispose();
            Assert.False(vm.IsPipelineRunning);
            Assert.NotEmpty(vm.Tab1Logs);
        }

        private sealed class NeverDrainingSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
                // Intentionally does not execute on the blocked thread.
            }
        }
    }
}
