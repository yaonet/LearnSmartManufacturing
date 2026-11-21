using LearnSmartManufacturing.Models.Motion;

namespace LearnSmartManufacturing.Tests.Models
{
    public class MotionTrajectoryPlannerTests
    {
        [Theory]
        [InlineData(100, 100, 400, 2000, true)]   // Full trapezoid (3 segments)
        [InlineData(10, 100, 400, 2000, false)]   // Short move: triangle (2 segments)
        [InlineData(-80, 100, 400, 2000, true)]   // Negative direction
        public void Trapezoidal_ReachesExactTargetWithinKinematicLimits(
            double distance, double vMax, double aMax, double jMax, bool expectedCruise)
        {
            var plan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(
                distance, vMax, aMax, jMax, TrajectoryProfileType.Trapezoidal));

            Assert.Equal(distance, plan.TotalDistanceMm, precision: 9);
            Assert.Equal(expectedCruise, plan.ReachedCruiseVelocity);
            Assert.True(plan.PeakVelocityMmPerSec <= vMax + 1e-9);
            Assert.True(plan.PeakAccelerationMmPerSec2 <= aMax + 1e-9);

            // Verify raw chained segment endpoint (before Evaluate end-snapping)
            var lastSeg = plan.Segments[^1];
            var rawEnd = lastSeg.EvaluateLocal(lastSeg.DurationSec);
            Assert.Equal(distance, rawEnd.PositionMm, precision: 8);
            Assert.Equal(0.0, rawEnd.VelocityMmPerSec, precision: 8);
        }

        [Theory]
        [InlineData(150, 100, 400, 2000, 7, true)]   // Sub-case 1: reaches vMax and aMax (7 segments)
        [InlineData(200, 50, 400, 1000, 5, true)]    // Sub-case 2: reaches vMax, not aMax (vMax*jMax < aMax^2 => 5 segments)
        [InlineData(40, 200, 400, 4000, 6, false)]   // Sub-case 4: reaches aMax, not vMax (6 segments)
        [InlineData(2, 100, 400, 2000, 4, false)]    // Sub-case 3: reaches neither vMax nor aMax (4 segments)
        [InlineData(-120, 100, 400, 2000, 7, true)]  // Negative direction 7-segment S-curve
        public void SCurve_AllSubCases_ReachExactTargetWithZeroEndVelAndAccel(
            double distance, double vMax, double aMax, double jMax, int expectedSegments, bool expectedCruise)
        {
            var plan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(
                distance, vMax, aMax, jMax, TrajectoryProfileType.SCurve));

            Assert.Equal(expectedSegments, plan.Segments.Count);
            Assert.Equal(expectedCruise, plan.ReachedCruiseVelocity);
            Assert.True(plan.PeakVelocityMmPerSec <= vMax + 1e-9);
            Assert.True(plan.PeakAccelerationMmPerSec2 <= aMax + 1e-9);
            Assert.True(plan.PeakJerkMmPerSec3 <= jMax + 1e-9);

            // Verify continuity across all segment boundaries and exact analytical endpoint
            for (var i = 0; i < plan.Segments.Count - 1; i++)
            {
                var curEnd = plan.Segments[i].EvaluateLocal(plan.Segments[i].DurationSec);
                var nextStart = plan.Segments[i + 1].EvaluateLocal(0);
                Assert.Equal(curEnd.PositionMm, nextStart.PositionMm, precision: 8);
                Assert.Equal(curEnd.VelocityMmPerSec, nextStart.VelocityMmPerSec, precision: 8);
                Assert.Equal(curEnd.AccelerationMmPerSec2, nextStart.AccelerationMmPerSec2, precision: 8);
            }

            var last = plan.Segments[^1];
            var rawEnd = last.EvaluateLocal(last.DurationSec);
            Assert.Equal(distance, rawEnd.PositionMm, precision: 8);
            Assert.Equal(0.0, rawEnd.VelocityMmPerSec, precision: 8);
            Assert.Equal(0.0, rawEnd.AccelerationMmPerSec2, precision: 8);
        }

        [Fact]
        public void SCurve_HasLongerDurationThanTrapezoidalForSameLimits()
        {
            var tPlan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(100, 100, 400, 2000, TrajectoryProfileType.Trapezoidal));
            var sPlan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(100, 100, 400, 2000, TrajectoryProfileType.SCurve));

            Assert.True(sPlan.TotalDurationSec > tPlan.TotalDurationSec);
        }

        [Fact]
        public void ZeroDistance_ReturnsEmptyZeroDurationPlan()
        {
            var plan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(0, 100, 400, 2000, TrajectoryProfileType.SCurve));

            Assert.Equal(0, plan.TotalDurationSec);
            Assert.Empty(plan.Segments);
            Assert.Equal(0, plan.Evaluate(0.5).PositionMm);
        }

        [Theory]
        [InlineData(double.NaN, 100, 400, 2000)]
        [InlineData(50, 0, 400, 2000)]
        [InlineData(50, 100, -1, 2000)]
        [InlineData(50, 100, 400, 0)]
        public void InvalidParameters_ThrowArgumentOutOfRange(double dist, double v, double a, double j)
        {
            var p = new MotionProfileParameters(dist, v, a, j, TrajectoryProfileType.SCurve);
            Assert.Throws<ArgumentOutOfRangeException>(() => MotionTrajectoryPlanner.Plan(p));
        }
    }
}
