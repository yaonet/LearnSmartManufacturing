using LearnSmartManufacturing.Models.Motion;
using LearnSmartManufacturing.Services.Motion;

namespace LearnSmartManufacturing.Tests.Services
{
    public class SimulatedMotionAxisTests
    {
        private static void StepFor(SimulatedMotionAxis axis, double seconds, double dt = 0.005)
        {
            var steps = (int)Math.Ceiling(seconds / dt);
            for (var i = 0; i < steps; i++)
            {
                axis.Step(dt);
            }
        }

        [Fact]
        public async Task SafetyInterlocks_BlockMotionBeforeEnableAndAbsoluteBeforeHoming()
        {
            using var axis = new SimulatedMotionAxis(startBackgroundLoop: false);

            // Disabled: cannot JOG, Home, Rel, or Abs
            Assert.False(axis.StartJog(1, 20, 200));
            Assert.False(await axis.HomeAsync(HomingMode.OriginSwitchOnly, 60, 8, 400));
            Assert.False(await axis.MoveRelativeAsync(10, 50, 200, 1000, TrajectoryProfileType.SCurve));
            Assert.False(await axis.MoveAbsoluteAsync(10, 50, 200, 1000, TrajectoryProfileType.SCurve));

            // Enabled but not homed: Abs is still blocked, Rel is allowed
            Assert.True(axis.ServoOn());
            Assert.Equal(AxisState.Standstill, axis.GetSnapshot().State);
            Assert.False(await axis.MoveAbsoluteAsync(10, 50, 200, 1000, TrajectoryProfileType.SCurve));
        }

        [Theory]
        [InlineData(HomingMode.NegativeLimitThenOrigin)]
        [InlineData(HomingMode.OriginSwitchOnly)]
        public async Task HomeAsync_CompletesToZeroAndEstablishesHomedFlag(HomingMode mode)
        {
            using var axis = new SimulatedMotionAxis(startBackgroundLoop: false);
            axis.ServoOn();

            var homeTask = axis.HomeAsync(mode, 80, 10, 500);
            Assert.Equal(AxisState.Homing, axis.GetSnapshot().State);

            StepFor(axis, 5.0);

            Assert.True(await homeTask);
            var snap = axis.GetSnapshot();
            Assert.Equal(AxisState.Standstill, snap.State);
            Assert.True(snap.IsHomed);
            Assert.True(snap.IsInPosition);
            Assert.Equal(0.0, snap.CommandPositionMm, precision: 6);
            Assert.Equal(0.0, snap.ActualPositionMm, precision: 6);
        }

        [Fact]
        public async Task MoveAbsoluteAndRelative_ReachTargetWithSettledFollowingError()
        {
            using var axis = new SimulatedMotionAxis(startBackgroundLoop: false);
            axis.ServoOn();
            var homeTask = axis.HomeAsync(HomingMode.OriginSwitchOnly, 80, 10, 500);
            StepFor(axis, 3.0);
            Assert.True(await homeTask);

            // Reject target outside soft limits
            Assert.False(await axis.MoveAbsoluteAsync(250, 100, 400, 2000, TrajectoryProfileType.SCurve));

            // Valid absolute move
            var absTask = axis.MoveAbsoluteAsync(100, 100, 400, 2000, TrajectoryProfileType.SCurve);
            Assert.Equal(AxisState.DiscreteMotion, axis.GetSnapshot().State);

            // Mid-move has realistic non-zero following error
            StepFor(axis, 0.5);
            Assert.True(Math.Abs(axis.GetSnapshot().FollowingErrorMm) > 0.001);

            // Finish move & settle
            StepFor(axis, 2.0);
            Assert.True(await absTask);
            var snap = axis.GetSnapshot();
            Assert.Equal(100.0, snap.CommandPositionMm, precision: 6);
            Assert.True(snap.IsInPosition);

            // Relative negative move
            var relTask = axis.MoveRelativeAsync(-25, 100, 400, 2000, TrajectoryProfileType.Trapezoidal);
            StepFor(axis, 2.0);
            Assert.True(await relTask);
            Assert.Equal(75.0, axis.GetSnapshot().CommandPositionMm, precision: 6);
        }

        [Fact]
        public void HardLimitTrip_AllowsOnlyReverseJogRecoveryAfterReset()
        {
            using var axis = new SimulatedMotionAxis(startBackgroundLoop: false);
            axis.ServoOn();

            // JOG positive until hitting +EL (210 mm)
            Assert.True(axis.StartJog(1, 150, 800));
            StepFor(axis, 2.0);

            var tripped = axis.GetSnapshot();
            Assert.Equal(AxisState.ErrorStop, tripped.State);
            Assert.Equal(MotionAlarmCode.PositiveLimit, tripped.AlarmCode);
            Assert.True(tripped.PositiveLimit);

            // Cannot re-enable before reset
            Assert.False(axis.ServoOn());

            // Reset & re-enable: still physically on +EL
            axis.ResetAlarm();
            Assert.True(axis.ServoOn());
            Assert.True(axis.GetSnapshot().PositiveLimit);

            // Positive JOG blocked, negative JOG allowed to back off +EL
            Assert.False(axis.StartJog(1, 40, 400));
            Assert.True(axis.StartJog(-1, 40, 400));
            StepFor(axis, 0.5);
            axis.StopJog();
            StepFor(axis, 0.5);

            var recovered = axis.GetSnapshot();
            Assert.False(recovered.PositiveLimit);
            Assert.Equal(AxisState.Standstill, recovered.State);
        }

        [Fact]
        public async Task EmergencyStop_CancelsMoveAndInvalidatesHoming()
        {
            using var axis = new SimulatedMotionAxis(startBackgroundLoop: false);
            axis.ServoOn();
            var homeTask = axis.HomeAsync(HomingMode.OriginSwitchOnly, 80, 10, 500);
            StepFor(axis, 3.0);
            Assert.True(await homeTask);

            var moveTask = axis.MoveAbsoluteAsync(150, 100, 400, 2000, TrajectoryProfileType.SCurve);
            StepFor(axis, 0.2);

            axis.EmergencyStop();

            Assert.False(await moveTask);
            var snap = axis.GetSnapshot();
            Assert.Equal(AxisState.ErrorStop, snap.State);
            Assert.Equal(MotionAlarmCode.EmergencyStop, snap.AlarmCode);
            Assert.False(snap.IsServoOn);
            Assert.False(snap.IsHomed);
        }

        [Fact]
        public async Task ControlledStop_DeceleratesAndKeepsServoAndHoming()
        {
            using var axis = new SimulatedMotionAxis(startBackgroundLoop: false);
            axis.ServoOn();
            var homeTask = axis.HomeAsync(HomingMode.OriginSwitchOnly, 80, 10, 500);
            StepFor(axis, 3.0);
            Assert.True(await homeTask);

            var moveTask = axis.MoveAbsoluteAsync(180, 120, 400, 2000, TrajectoryProfileType.SCurve);
            StepFor(axis, 0.4);

            axis.Stop();
            Assert.False(await moveTask);
            Assert.Equal(AxisState.Stopping, axis.GetSnapshot().State);

            StepFor(axis, 1.0);
            var snap = axis.GetSnapshot();
            Assert.Equal(AxisState.Standstill, snap.State);
            Assert.True(snap.IsServoOn);
            Assert.True(snap.IsHomed);
            Assert.Equal(0.0, snap.VelocityMmPerSec, precision: 6);
        }

        [Theory]
        [InlineData(MotionAlarmCode.PositiveLimit)]
        [InlineData(MotionAlarmCode.NegativeLimit)]
        [InlineData(MotionAlarmCode.FollowingErrorExceeded)]
        [InlineData(MotionAlarmCode.ServoFault)]
        public void InjectFault_TransitionsToErrorStopWithAlarm(MotionAlarmCode code)
        {
            using var axis = new SimulatedMotionAxis(startBackgroundLoop: false);
            axis.ServoOn();

            axis.InjectFault(code);

            var snap = axis.GetSnapshot();
            Assert.Equal(AxisState.ErrorStop, snap.State);
            Assert.Equal(code, snap.AlarmCode);
            Assert.False(snap.IsServoOn);
        }
    }
}
