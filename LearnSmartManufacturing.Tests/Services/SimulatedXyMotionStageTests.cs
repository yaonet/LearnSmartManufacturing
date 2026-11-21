using LearnSmartManufacturing.Models.Motion;
using LearnSmartManufacturing.Services.Motion;

namespace LearnSmartManufacturing.Tests.Services
{
    public class SimulatedXyMotionStageTests
    {
        private static void StepFor(SimulatedXyMotionStage stage, double seconds, double dt = 0.005)
        {
            var steps = (int)Math.Ceiling(seconds / dt);
            for (var i = 0; i < steps; i++)
            {
                stage.Step(dt);
            }
        }

        [Fact]
        public async Task MoveToWithPso_FiresExactUniformlySpacedFlyCaptureTriggers()
        {
            using var stage = new SimulatedXyMotionStage(startBackgroundLoop: false);

            // Move from default (20, 20) to (170, 20): exact 150 mm horizontal scan with 15 mm PSO pitch -> 11 triggers (0, 15, ..., 150)
            var task = stage.MoveToAsync(
                new Point2D(170, 20),
                XyMotionMode.LinearInterpolation,
                feedVelocityMmPerSec: 150,
                maxAccelMmPerSec2: 600,
                maxJerkMmPerSec3: 3000,
                TrajectoryProfileType.SCurve,
                psoEnabled: true,
                psoIntervalMm: 15.0);

            StepFor(stage, 3.0);

            Assert.True(await task);
            var snap = stage.GetSnapshot();
            Assert.Equal(170.0, snap.CommandX, precision: 6);
            Assert.Equal(20.0, snap.CommandY, precision: 6);
            Assert.Equal(11, snap.PsoTriggerCount);
            Assert.Equal(0, snap.StopCaptureCount);

            for (var i = 0; i < snap.Triggers.Count; i++)
            {
                Assert.True(snap.Triggers[i].IsPso);
                Assert.Equal(20.0 + i * 15.0, snap.Triggers[i].Position.X, precision: 6);
                Assert.Equal(20.0, snap.Triggers[i].Position.Y, precision: 6);
            }
        }

        [Fact]
        public async Task MoveTo_RejectsOutOfWorkspaceTargetAndBulgingArc()
        {
            using var stage = new SimulatedXyMotionStage(startBackgroundLoop: false);

            // Out of bounds target
            Assert.False(await stage.MoveToAsync(new Point2D(250, 50), XyMotionMode.LinearInterpolation, 100, 400, 2000, TrajectoryProfileType.SCurve));

            // Semicircle from (20, 20) to (180, 20) curving CCW downward drops below y=0 -> rejected!
            Assert.False(await stage.MoveToAsync(new Point2D(180, 20), XyMotionMode.CircularArcCcw, 100, 400, 2000, TrajectoryProfileType.SCurve, arcRadiusMm: 80));
        }

        [Fact]
        public async Task EmergencyStop_RequiresResetEnableAndHomeBeforeMoving()
        {
            using var stage = new SimulatedXyMotionStage(startBackgroundLoop: false);

            stage.EmergencyStop();
            Assert.Equal(AxisState.ErrorStop, stage.GetSnapshot().State);
            Assert.False(stage.ServoOn());

            stage.ResetAlarm();
            Assert.True(stage.ServoOn());

            // Not homed yet -> MoveTo blocked
            Assert.False(await stage.MoveToAsync(new Point2D(80, 60), XyMotionMode.LinearInterpolation, 100, 400, 2000, TrajectoryProfileType.SCurve));

            // Home to (0, 0)
            var homeTask = stage.HomeAsync(100);
            StepFor(stage, 2.0);
            Assert.True(await homeTask);
            Assert.True(stage.GetSnapshot().IsHomed);
            Assert.Equal(0.0, stage.GetSnapshot().CommandX, precision: 6);
            Assert.Equal(0.0, stage.GetSnapshot().CommandY, precision: 6);

            // Now MoveTo succeeds
            var moveTask = stage.MoveToAsync(new Point2D(60, 40), XyMotionMode.LinearInterpolation, 100, 400, 2000, TrajectoryProfileType.SCurve);
            StepFor(stage, 2.0);
            Assert.True(await moveTask);
            Assert.Equal(60.0, stage.GetSnapshot().CommandX, precision: 6);
            Assert.Equal(40.0, stage.GetSnapshot().CommandY, precision: 6);
        }
    }
}
