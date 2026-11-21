using LearnSmartManufacturing.Models.Motion;
using LearnSmartManufacturing.Services.Motion;
using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class MotionControlViewModelTests
    {
        [Fact]
        public void Constructor_InitializesSCurveAndComparisonPlans()
        {
            using var axis = new SimulatedMotionAxis(startBackgroundLoop: false);
            using var vm = new MotionControlViewModel(axis);

            Assert.NotNull(vm.PrimaryPlan);
            Assert.NotNull(vm.ComparisonPlan);
            Assert.Equal(TrajectoryProfileType.SCurve, vm.PrimaryPlan.ProfileType);
            Assert.Equal(TrajectoryProfileType.Trapezoidal, vm.ComparisonPlan.ProfileType);
            Assert.Equal(AxisState.Disabled, vm.State);
            Assert.True(vm.CanInjectFault);
        }

        [Fact]
        public void SwitchingProfileAndParameters_RebuildsPreviewPlans()
        {
            using var axis = new SimulatedMotionAxis(startBackgroundLoop: false);
            using var vm = new MotionControlViewModel(axis);

            var initialDuration = vm.PrimaryPlan!.TotalDurationSec;

            vm.SelectedProfile = vm.ProfileOptions.First(p => p.Value == TrajectoryProfileType.Trapezoidal);
            Assert.Equal(TrajectoryProfileType.Trapezoidal, vm.PrimaryPlan!.ProfileType);
            Assert.Equal(TrajectoryProfileType.SCurve, vm.ComparisonPlan!.ProfileType);
            Assert.True(vm.PrimaryPlan.TotalDurationSec < initialDuration);

            vm.MaxVelocityMmPerSec = 200;
            Assert.True(vm.PrimaryPlan.TotalDurationSec < initialDuration);
        }

        [Fact]
        public void Commands_UpdateStateTelemetryAndEventLogs()
        {
            using var axis = new SimulatedMotionAxis(startBackgroundLoop: false);
            using var vm = new MotionControlViewModel(axis);

            vm.ServoOnCommand.Execute(null);
            Assert.True(vm.IsServoOn);
            Assert.Equal(AxisState.Standstill, vm.State);
            Assert.NotEmpty(vm.Logs);

            vm.PulsePerMmText = "2000";
            Assert.Equal("90,000 pulse", vm.CommandPulseText);

            vm.InjectServoFaultCommand.Execute(null);
            Assert.True(vm.HasAlarm);
            Assert.Equal(AxisState.ErrorStop, vm.State);

            vm.ResetAlarmCommand.Execute(null);
            Assert.False(vm.HasAlarm);
            Assert.Equal(AxisState.Disabled, vm.State);

            vm.ClearLogsCommand.Execute(null);
            Assert.Empty(vm.Logs);
        }
    }
}
