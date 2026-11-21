using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class MvvmViewModelTests
    {
        [Fact]
        public void InitialState_IsValidAndCleanWithCommandsDisabled()
        {
            using var vm = new MvvmViewModel(deployDelayMs: 0);

            Assert.False(vm.Editor.HasErrors);
            Assert.False(vm.Editor.IsDirty);
            Assert.False(vm.Editor.DeployRecipeCommand.CanExecute(null));
            Assert.False(vm.Editor.RevertCommand.CanExecute(null));
            Assert.True(vm.Monitor.IsActive);
        }

        [Fact]
        public void CrossPropertyValidation_RevalidatesAlarmTempWhenTargetChanges()
        {
            using var vm = new MvvmViewModel(deployDelayMs: 0);

            // Raising TargetTemp to 245 breaks AlarmTemp >= Target + 10 (250 < 255)
            vm.Editor.TargetTempText = "245";
            Assert.True(vm.Editor.HasErrors);
            Assert.Contains("跨属性校验失败", vm.Editor.ErrorSummary);
            Assert.False(vm.Editor.DeployRecipeCommand.CanExecute(null));
            Assert.True(vm.Editor.RevertCommand.CanExecute(null));

            // Adjusting AlarmTemp to 260 clears errors and enables Deploy
            vm.Editor.AlarmTempText = "260";
            Assert.False(vm.Editor.HasErrors);
            Assert.True(vm.Editor.IsDirty);
            Assert.True(vm.Editor.DeployRecipeCommand.CanExecute(null));
        }

        [Fact]
        public void FillInvalidSampleAndRevert_TogglesErrorsAndRestoresSnapshot()
        {
            using var vm = new MvvmViewModel(deployDelayMs: 0);

            vm.Editor.FillInvalidSampleCommand.Execute(null);
            Assert.True(vm.Editor.HasErrors);
            Assert.True(vm.Editor.GetErrors().Count() >= 4);
            Assert.False(vm.Editor.DeployRecipeCommand.CanExecute(null));

            vm.Editor.RevertCommand.Execute(null);
            Assert.False(vm.Editor.HasErrors);
            Assert.False(vm.Editor.IsDirty);
            Assert.Equal("SMT-2026", vm.Editor.RecipeCode);
        }

        [Fact]
        public async Task DeployRecipe_NotifiesMonitorOnlyWhileIsActiveIsTrue()
        {
            using var vm = new MvvmViewModel(deployDelayMs: 0);

            // Active monitor receives deployed recipe via WeakReferenceMessenger
            vm.Editor.RecipeCode = "AOI-3000";
            vm.Editor.TargetTempText = "230";
            vm.Editor.AlarmTempText = "260";
            await vm.Editor.DeployRecipeCommand.ExecuteAsync(null);

            Assert.False(vm.Editor.IsDirty);
            Assert.Equal("AOI-3000", vm.Monitor.ActiveRecipeCode);
            Assert.Equal(2, vm.Monitor.ActiveRecipe.Version);
            Assert.Equal(1, vm.Monitor.ReceivedMessageCount);

            // Deactivating ObservableRecipient unsubscribes automatically
            vm.Monitor.IsActive = false;
            vm.Editor.RecipeCode = "AOI-4000";
            await vm.Editor.DeployRecipeCommand.ExecuteAsync(null);

            Assert.Equal("AOI-3000", vm.Monitor.ActiveRecipeCode);
            Assert.Equal(1, vm.Monitor.ReceivedMessageCount);
        }

        [Fact]
        public void BroadcastOverTempAlarm_ReachesEditorViaMessenger()
        {
            using var vm = new MvvmViewModel(deployDelayMs: 0);

            Assert.False(vm.Editor.HasLineAlarm);
            vm.Monitor.BroadcastOverTempAlarmCommand.Execute(null);

            Assert.True(vm.Editor.HasLineAlarm);
            Assert.Contains("AL-201", vm.Editor.LastLineAlarmText);

            vm.Editor.DismissLineAlarmCommand.Execute(null);
            Assert.False(vm.Editor.HasLineAlarm);
        }

        [Fact]
        public async Task DeployRecipeCancelCommand_CancelsInFlightDeployment()
        {
            using var vm = new MvvmViewModel(deployDelayMs: 500);
            vm.Editor.RecipeCode = "CNC-9999";

            var deployTask = vm.Editor.DeployRecipeCommand.ExecuteAsync(null);
            await Task.Delay(40);
            vm.Editor.DeployRecipeCancelCommand.Execute(null);
            await deployTask;

            Assert.True(vm.Editor.IsDirty);
            Assert.Equal(0, vm.Monitor.ReceivedMessageCount);
            Assert.Contains("取消", vm.Editor.DeployProgressText);
        }

        [Fact]
        public void Dispose_DeactivatesMonitorAndUnregistersEditor()
        {
            var vm = new MvvmViewModel(deployDelayMs: 0);
            vm.Dispose();

            Assert.False(vm.Monitor.IsActive);
            vm.Monitor.BroadcastOverTempAlarmCommand.Execute(null);
            Assert.False(vm.Editor.HasLineAlarm);
        }
    }
}
