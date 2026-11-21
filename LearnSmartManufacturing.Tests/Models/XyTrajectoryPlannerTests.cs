using LearnSmartManufacturing.Models.Motion;

namespace LearnSmartManufacturing.Tests.Models
{
    public class XyTrajectoryPlannerTests
    {
        [Fact]
        public void LinearInterpolation_StaysStrictlyCollinearWithExactEndpoints()
        {
            var start = new Point2D(20, 30);
            var end = new Point2D(170, 110);

            var plan = XyTrajectoryPlanner.Plan(start, end, XyMotionMode.LinearInterpolation,
                140, 600, 3000, TrajectoryProfileType.SCurve);

            Assert.Equal(start.DistanceTo(end), plan.PathLengthMm, precision: 8);
            Assert.Equal(start.X, plan.Evaluate(0).X, precision: 8);
            Assert.Equal(start.Y, plan.Evaluate(0).Y, precision: 8);
            Assert.Equal(end.X, plan.Evaluate(plan.TotalDurationSec).X, precision: 8);
            Assert.Equal(end.Y, plan.Evaluate(plan.TotalDurationSec).Y, precision: 8);

            // Every sampled point must have zero cross product with (end - start)
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            foreach (var p in plan.SamplePolyline(64))
            {
                var cross = (p.X - start.X) * dy - (p.Y - start.Y) * dx;
                Assert.True(Math.Abs(cross) < 1e-7, $"Point ({p.X},{p.Y}) deviated from straight line by cross={cross}");
            }
        }

        [Fact]
        public void IndependentPtp_OnUnequalDiagonal_ProducesDogLegAndReachesExactTarget()
        {
            var start = new Point2D(20, 20);
            var end = new Point2D(180, 120);

            var plan = XyTrajectoryPlanner.Plan(start, end, XyMotionMode.IndependentPtp,
                120, 500, 2500, TrajectoryProfileType.SCurve);

            Assert.NotNull(plan.XPlan);
            Assert.NotNull(plan.YPlan);
            Assert.True(plan.YPlan.TotalDurationSec < plan.XPlan.TotalDurationSec);

            // Mid-path must deviate from the straight diagonal (dog-leg)
            var mid = plan.Evaluate(plan.YPlan.TotalDurationSec);
            Assert.Equal(end.Y, mid.Y, precision: 6);
            Assert.True(mid.X < end.X - 10.0);

            // Final endpoint is exact
            var final = plan.Evaluate(plan.TotalDurationSec);
            Assert.Equal(end.X, final.X, precision: 8);
            Assert.Equal(end.Y, final.Y, precision: 8);
        }

        [Theory]
        [InlineData(XyMotionMode.CircularArcCw, 65)]
        [InlineData(XyMotionMode.CircularArcCcw, 65)]
        [InlineData(XyMotionMode.CircularArcCw, 45)]   // Exact semicircle for chord=90
        [InlineData(XyMotionMode.CircularArcCcw, 45)]  // Exact semicircle for chord=90
        public void CircularArc_StaysOnExactCircleWithMatchingTangentialFeedSpeed(XyMotionMode mode, double radius)
        {
            var start = new Point2D(150, 30);
            var end = new Point2D(150, 120);

            var plan = XyTrajectoryPlanner.Plan(start, end, mode, 120, 500, 2500, TrajectoryProfileType.SCurve, radius);

            Assert.NotNull(plan.ArcCenter);
            var center = plan.ArcCenter.Value;
            Assert.Equal(radius, plan.ArcRadiusMm, precision: 8);

            // Check direction of sweep
            if (mode == XyMotionMode.CircularArcCw)
            {
                Assert.True(plan.ArcSweepAngleRad < 0);
            }
            else
            {
                Assert.True(plan.ArcSweepAngleRad > 0);
            }

            // Every sampled point lies on the circle of radius R around center
            for (var i = 0; i <= 50; i++)
            {
                var t = plan.TotalDurationSec * i / 50.0;
                var s = plan.Evaluate(t);
                var distFromCenter = center.DistanceTo(new Point2D(s.X, s.Y));
                Assert.Equal(radius, distFromCenter, precision: 7);

                var expectedSpeed = plan.PathPlan!.Evaluate(t).VelocityMmPerSec;
                Assert.Equal(expectedSpeed, s.FeedSpeedMmPerSec, precision: 7);
            }

            var final = plan.Evaluate(plan.TotalDurationSec);
            Assert.Equal(end.X, final.X, precision: 6);
            Assert.Equal(end.Y, final.Y, precision: 6);

            // Exact PSO path distance evaluation lies on the circle
            var midArc = plan.EvaluateAtPathDistance(plan.PathLengthMm * 0.5);
            Assert.Equal(radius, center.DistanceTo(midArc), precision: 7);
        }
    }
}
