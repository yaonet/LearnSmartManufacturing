namespace LearnSmartManufacturing.Models.Motion
{
    public readonly record struct Point2D(double X, double Y)
    {
        public double DistanceTo(Point2D other) => double.Hypot(other.X - X, other.Y - Y);
    }

    public enum XyMotionMode
    {
        IndependentPtp,
        LinearInterpolation,
        CircularArcCw,
        CircularArcCcw,
    }

    public readonly record struct TriggerMarker(Point2D Position, bool IsPso, int Sequence);

    public sealed record TeachPoint(
        int Index,
        string Name,
        double X,
        double Y,
        XyMotionMode Mode,
        double ArcRadiusMm = 0,
        bool CaptureTrigger = false,
        int DwellMs = 100)
    {
        public string ModeShort => Mode switch
        {
            XyMotionMode.IndependentPtp => "PTP",
            XyMotionMode.LinearInterpolation => "G01 直线",
            XyMotionMode.CircularArcCw => "G02 顺圆",
            XyMotionMode.CircularArcCcw => "G03 逆圆",
            _ => Mode.ToString(),
        };

        public string CoordText => $"({X:F1}, {Y:F1})";
        public string TriggerText => CaptureTrigger ? $"停拍 {DwellMs}ms" : "—";
    }

    public readonly record struct XySample(
        double TimeSec,
        double X,
        double Y,
        double Vx,
        double Vy,
        double PathDistanceMm)
    {
        public double FeedSpeedMmPerSec => double.Hypot(Vx, Vy);
    }

    public sealed record XyTrajectoryPlan
    {
        public XyMotionMode Mode { get; init; }
        public Point2D Start { get; init; }
        public Point2D End { get; init; }
        public double PathLengthMm { get; init; }
        public double TotalDurationSec { get; init; }

        // Linear / Arc interpolation uses a 1D path speed profile.
        public TrajectoryPlan? PathPlan { get; init; }

        // Independent PTP uses separate 1D profiles on X and Y.
        public TrajectoryPlan? XPlan { get; init; }
        public TrajectoryPlan? YPlan { get; init; }

        // Circular arc parameters (standard Cartesian XY: +X right, +Y up).
        public Point2D? ArcCenter { get; init; }
        public double ArcRadiusMm { get; init; }
        public double ArcStartAngleRad { get; init; }
        public double ArcSweepAngleRad { get; init; }

        public XySample Evaluate(double timeSec)
        {
            if (TotalDurationSec <= 0 || timeSec <= 0)
            {
                return new XySample(0, Start.X, Start.Y, 0, 0, 0);
            }

            if (timeSec >= TotalDurationSec)
            {
                return new XySample(TotalDurationSec, End.X, End.Y, 0, 0, PathLengthMm);
            }

            return Mode switch
            {
                XyMotionMode.IndependentPtp => EvaluatePtp(timeSec),
                XyMotionMode.LinearInterpolation => EvaluateLinear(timeSec),
                XyMotionMode.CircularArcCw or XyMotionMode.CircularArcCcw => EvaluateArc(timeSec),
                _ => new XySample(timeSec, End.X, End.Y, 0, 0, PathLengthMm),
            };
        }

        /// <summary>Evaluates exact geometric point at cumulative path distance <paramref name="sMm"/> (used by PSO comparator).</summary>
        public Point2D EvaluateAtPathDistance(double sMm)
        {
            if (PathLengthMm <= 1e-9 || sMm <= 0)
            {
                return Start;
            }

            if (sMm >= PathLengthMm)
            {
                return End;
            }

            switch (Mode)
            {
                case XyMotionMode.LinearInterpolation:
                {
                    var frac = sMm / PathLengthMm;
                    return new Point2D(Start.X + (End.X - Start.X) * frac, Start.Y + (End.Y - Start.Y) * frac);
                }

                case XyMotionMode.CircularArcCw:
                case XyMotionMode.CircularArcCcw:
                {
                    var center = ArcCenter!.Value;
                    var dir = Math.Sign(ArcSweepAngleRad);
                    var theta = ArcStartAngleRad + dir * (sMm / ArcRadiusMm);
                    return new Point2D(center.X + ArcRadiusMm * Math.Cos(theta), center.Y + ArcRadiusMm * Math.Sin(theta));
                }

                default:
                {
                    var frac = sMm / PathLengthMm;
                    var sample = Evaluate(frac * TotalDurationSec);
                    return new Point2D(sample.X, sample.Y);
                }
            }
        }

        public IReadOnlyList<Point2D> SamplePolyline(int pointCount = 96)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(pointCount, 2);

            if (TotalDurationSec <= 0)
            {
                return [Start, End];
            }

            var points = new Point2D[pointCount];
            for (var i = 0; i < pointCount; i++)
            {
                var t = TotalDurationSec * i / (pointCount - 1);
                var s = Evaluate(t);
                points[i] = new Point2D(s.X, s.Y);
            }

            return points;
        }

        private XySample EvaluatePtp(double timeSec)
        {
            var sx = XPlan!.Evaluate(timeSec);
            var sy = YPlan!.Evaluate(timeSec);
            var dist = double.Hypot(sx.PositionMm, sy.PositionMm);
            return new XySample(timeSec, Start.X + sx.PositionMm, Start.Y + sy.PositionMm, sx.VelocityMmPerSec, sy.VelocityMmPerSec, dist);
        }

        private XySample EvaluateLinear(double timeSec)
        {
            var s = PathPlan!.Evaluate(timeSec);
            var ux = (End.X - Start.X) / PathLengthMm;
            var uy = (End.Y - Start.Y) / PathLengthMm;
            return new XySample(
                timeSec,
                Start.X + ux * s.PositionMm,
                Start.Y + uy * s.PositionMm,
                ux * s.VelocityMmPerSec,
                uy * s.VelocityMmPerSec,
                s.PositionMm);
        }

        private XySample EvaluateArc(double timeSec)
        {
            var s = PathPlan!.Evaluate(timeSec);
            var center = ArcCenter!.Value;
            var dir = Math.Sign(ArcSweepAngleRad);
            var theta = ArcStartAngleRad + dir * (s.PositionMm / ArcRadiusMm);
            var x = center.X + ArcRadiusMm * Math.Cos(theta);
            var y = center.Y + ArcRadiusMm * Math.Sin(theta);
            var vx = -dir * s.VelocityMmPerSec * Math.Sin(theta);
            var vy = dir * s.VelocityMmPerSec * Math.Cos(theta);
            return new XySample(timeSec, x, y, vx, vy, s.PositionMm);
        }
    }

    /// <summary>2D trajectory planner for independent PTP, synchronized linear (G01), and circular arc (G02/G03) interpolation.</summary>
    public static class XyTrajectoryPlanner
    {
        public static XyTrajectoryPlan Plan(
            Point2D start,
            Point2D end,
            XyMotionMode mode,
            double feedVelocityMmPerSec,
            double maxAccelMmPerSec2,
            double maxJerkMmPerSec3,
            TrajectoryProfileType profileType,
            double arcRadiusMm = 0)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var chord = double.Hypot(dx, dy);

            if (chord <= 1e-9)
            {
                return new XyTrajectoryPlan
                {
                    Mode = mode,
                    Start = start,
                    End = end,
                    PathLengthMm = 0,
                    TotalDurationSec = 0,
                };
            }

            return mode switch
            {
                XyMotionMode.IndependentPtp => PlanPtp(start, end, dx, dy, feedVelocityMmPerSec, maxAccelMmPerSec2, maxJerkMmPerSec3, profileType),
                XyMotionMode.LinearInterpolation => PlanLinear(start, end, chord, feedVelocityMmPerSec, maxAccelMmPerSec2, maxJerkMmPerSec3, profileType),
                XyMotionMode.CircularArcCw => PlanArc(start, end, chord, clockwise: true, arcRadiusMm, feedVelocityMmPerSec, maxAccelMmPerSec2, maxJerkMmPerSec3, profileType),
                XyMotionMode.CircularArcCcw => PlanArc(start, end, chord, clockwise: false, arcRadiusMm, feedVelocityMmPerSec, maxAccelMmPerSec2, maxJerkMmPerSec3, profileType),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
        }

        private static XyTrajectoryPlan PlanPtp(
            Point2D start,
            Point2D end,
            double dx,
            double dy,
            double vMax,
            double aMax,
            double jMax,
            TrajectoryProfileType profileType)
        {
            var xPlan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(dx, vMax, aMax, jMax, profileType));
            var yPlan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(dy, vMax, aMax, jMax, profileType));
            var duration = Math.Max(xPlan.TotalDurationSec, yPlan.TotalDurationSec);

            // Actual geometric path length for PTP (approx via polyline integral, exact for symmetric moves).
            var plan = new XyTrajectoryPlan
            {
                Mode = XyMotionMode.IndependentPtp,
                Start = start,
                End = end,
                PathLengthMm = double.Hypot(dx, dy),
                TotalDurationSec = duration,
                XPlan = xPlan,
                YPlan = yPlan,
            };

            // Recompute exact polyline path length for accurate telemetry.
            var pts = plan.SamplePolyline(128);
            double actualLen = 0;
            for (var i = 1; i < pts.Count; i++)
            {
                actualLen += pts[i - 1].DistanceTo(pts[i]);
            }

            return plan with { PathLengthMm = actualLen };
        }

        private static XyTrajectoryPlan PlanLinear(
            Point2D start,
            Point2D end,
            double length,
            double vMax,
            double aMax,
            double jMax,
            TrajectoryProfileType profileType)
        {
            var pathPlan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(length, vMax, aMax, jMax, profileType));
            return new XyTrajectoryPlan
            {
                Mode = XyMotionMode.LinearInterpolation,
                Start = start,
                End = end,
                PathLengthMm = length,
                TotalDurationSec = pathPlan.TotalDurationSec,
                PathPlan = pathPlan,
            };
        }

        private static XyTrajectoryPlan PlanArc(
            Point2D start,
            Point2D end,
            double chord,
            bool clockwise,
            double requestedRadiusMm,
            double vMax,
            double aMax,
            double jMax,
            TrajectoryProfileType profileType)
        {
            var halfChord = chord * 0.5;
            var radius = requestedRadiusMm <= 0 ? chord * 0.65 : Math.Max(requestedRadiusMm, halfChord);
            var h = Math.Sqrt(Math.Max(0, radius * radius - halfChord * halfChord));

            var ux = (end.X - start.X) / chord;
            var uy = (end.Y - start.Y) / chord;
            // Left normal of start->end in Cartesian XY (+X right, +Y up)
            var nxLeft = -uy;
            var nyLeft = ux;

            // CCW minor arc places center to the left of start->end; CW minor arc places center to the right.
            var normalSign = clockwise ? -1.0 : 1.0;
            var cx = (start.X + end.X) * 0.5 + normalSign * h * nxLeft;
            var cy = (start.Y + end.Y) * 0.5 + normalSign * h * nyLeft;
            var center = new Point2D(cx, cy);

            var startAngle = Math.Atan2(start.Y - cy, start.X - cx);
            var endAngle = Math.Atan2(end.Y - cy, end.X - cx);

            double sweep;
            if (clockwise)
            {
                sweep = endAngle - startAngle;
                while (sweep >= 0)
                {
                    sweep -= 2.0 * Math.PI;
                }

                if (sweep < -Math.PI - 1e-9)
                {
                    sweep += 2.0 * Math.PI;
                }
            }
            else
            {
                sweep = endAngle - startAngle;
                while (sweep <= 0)
                {
                    sweep += 2.0 * Math.PI;
                }

                if (sweep > Math.PI + 1e-9)
                {
                    sweep -= 2.0 * Math.PI;
                }
            }

            // Handle exact semicircle (h == 0) cleanly.
            if (Math.Abs(h) <= 1e-9)
            {
                sweep = clockwise ? -Math.PI : Math.PI;
            }

            var arcLength = radius * Math.Abs(sweep);
            var pathPlan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(arcLength, vMax, aMax, jMax, profileType));

            return new XyTrajectoryPlan
            {
                Mode = clockwise ? XyMotionMode.CircularArcCw : XyMotionMode.CircularArcCcw,
                Start = start,
                End = end,
                PathLengthMm = arcLength,
                TotalDurationSec = pathPlan.TotalDurationSec,
                PathPlan = pathPlan,
                ArcCenter = center,
                ArcRadiusMm = radius,
                ArcStartAngleRad = startAngle,
                ArcSweepAngleRad = sweep,
            };
        }
    }

    public sealed record XyStageSnapshot(
        AxisState State,
        bool IsServoOn,
        bool IsHomed,
        MotionAlarmCode AlarmCode,
        string AlarmMessage,
        double CommandX,
        double CommandY,
        double ActualX,
        double ActualY,
        double Vx,
        double Vy,
        double ElapsedMoveTimeSec,
        XyTrajectoryPlan? ActivePlan,
        IReadOnlyList<Point2D> Trail,
        IReadOnlyList<TriggerMarker> Triggers)
    {
        public double FeedSpeedMmPerSec => double.Hypot(Vx, Vy);
        public double FollowingErrorMm => double.Hypot(CommandX - ActualX, CommandY - ActualY);
        public int StopCaptureCount => Triggers.Count(t => !t.IsPso);
        public int PsoTriggerCount => Triggers.Count(t => t.IsPso);
    }
}
