namespace LearnSmartManufacturing.Models.Motion
{
    /// <summary>
    /// Analytical point-to-point motion planner supporting trapezoidal (T-curve) and
    /// symmetric 7-segment jerk-limited (S-curve) velocity profiles.
    /// </summary>
    public static class MotionTrajectoryPlanner
    {
        private const double Epsilon = 1e-12;

        public static TrajectoryPlan Plan(MotionProfileParameters parameters)
        {
            if (!double.IsFinite(parameters.DistanceMm))
            {
                throw new ArgumentOutOfRangeException(nameof(parameters), "Distance must be finite.");
            }

            ValidateFinitePositive(parameters.MaxVelocityMmPerSec, nameof(parameters.MaxVelocityMmPerSec));
            ValidateFinitePositive(parameters.MaxAccelerationMmPerSec2, nameof(parameters.MaxAccelerationMmPerSec2));
            ValidateFinitePositive(parameters.MaxJerkMmPerSec3, nameof(parameters.MaxJerkMmPerSec3));

            var distance = Math.Abs(parameters.DistanceMm);
            if (distance <= 1e-9)
            {
                return new TrajectoryPlan
                {
                    ProfileType = parameters.ProfileType,
                    TotalDistanceMm = 0,
                    TotalDurationSec = 0,
                    PeakVelocityMmPerSec = 0,
                    PeakAccelerationMmPerSec2 = 0,
                    PeakJerkMmPerSec3 = 0,
                    ReachedCruiseVelocity = false,
                    Segments = [],
                };
            }

            var direction = Math.Sign(parameters.DistanceMm);
            return parameters.ProfileType switch
            {
                TrajectoryProfileType.Trapezoidal => PlanTrapezoidal(direction, distance, parameters.MaxVelocityMmPerSec, parameters.MaxAccelerationMmPerSec2),
                TrajectoryProfileType.SCurve => PlanSCurve(direction, distance, parameters.MaxVelocityMmPerSec, parameters.MaxAccelerationMmPerSec2, parameters.MaxJerkMmPerSec3),
                _ => throw new ArgumentOutOfRangeException(nameof(parameters.ProfileType)),
            };
        }

        private static TrajectoryPlan PlanTrapezoidal(int direction, double distance, double vMax, double aMax)
        {
            var triDistance = vMax * vMax / aMax;
            double tAcc;
            double tCruise;
            double vPeak;
            bool reachedCruise;

            if (distance >= triDistance)
            {
                tAcc = vMax / aMax;
                tCruise = (distance - triDistance) / vMax;
                vPeak = vMax;
                reachedCruise = tCruise > 1e-9;
            }
            else
            {
                vPeak = Math.Sqrt(distance * aMax);
                tAcc = vPeak / aMax;
                tCruise = 0;
                reachedCruise = false;
            }

            var segments = new List<TrajectorySegment>(3);
            double t = 0, p = 0, v = 0;

            void AddConstAcc(double duration, double acc)
            {
                if (duration <= Epsilon)
                {
                    return;
                }

                var seg = new TrajectorySegment(t, duration, p, v, acc, 0);
                segments.Add(seg);
                var end = seg.EvaluateLocal(duration);
                t = seg.EndTimeSec;
                p = end.PositionMm;
                v = end.VelocityMmPerSec;
            }

            AddConstAcc(tAcc, direction * aMax);
            AddConstAcc(tCruise, 0);
            AddConstAcc(tAcc, -direction * aMax);

            return new TrajectoryPlan
            {
                ProfileType = TrajectoryProfileType.Trapezoidal,
                TotalDistanceMm = direction * distance,
                TotalDurationSec = t,
                PeakVelocityMmPerSec = vPeak,
                PeakAccelerationMmPerSec2 = aMax,
                PeakJerkMmPerSec3 = 0,
                ReachedCruiseVelocity = reachedCruise,
                Segments = segments,
            };
        }

        private static TrajectoryPlan PlanSCurve(int direction, double distance, double vMax, double aMax, double jMax)
        {
            // Determine jerk (Tj) and constant-acceleration (Ta) durations required to reach vMax.
            double tJ;
            double tA;
            if (vMax * jMax >= aMax * aMax)
            {
                tJ = aMax / jMax;
                tA = vMax / aMax - tJ;
            }
            else
            {
                tJ = Math.Sqrt(vMax / jMax);
                tA = 0;
            }

            double tV;
            var minDistForVMax = vMax * (2.0 * tJ + tA);
            if (distance >= minDistForVMax)
            {
                tV = (distance - minDistForVMax) / vMax;
            }
            else
            {
                tV = 0;
                var minDistForAMax = 2.0 * aMax * aMax * aMax / (jMax * jMax);
                if (distance < minDistForAMax)
                {
                    // Short move: neither vMax nor aMax is reached (4-segment pure-jerk profile).
                    tJ = Math.Cbrt(distance / (2.0 * jMax));
                    tA = 0;
                }
                else
                {
                    // Medium move: aMax is reached, vMax is not reached (6-segment profile).
                    tJ = aMax / jMax;
                    tA = (-3.0 * tJ + Math.Sqrt(tJ * tJ + 4.0 * distance / aMax)) * 0.5;
                }
            }

            var aPeak = jMax * tJ;
            var vPeak = aPeak * (tJ + tA);

            var segments = new List<TrajectorySegment>(7);
            double t = 0, p = 0, v = 0, a = 0;

            void AddJerkSegment(double duration, double jerk)
            {
                if (duration <= Epsilon)
                {
                    return;
                }

                var seg = new TrajectorySegment(t, duration, p, v, a, jerk);
                segments.Add(seg);
                var end = seg.EvaluateLocal(duration);
                t = seg.EndTimeSec;
                p = end.PositionMm;
                v = end.VelocityMmPerSec;
                a = end.AccelerationMmPerSec2;
                // Eliminate tiny floating-point residual on zero-accel boundaries.
                if (Math.Abs(a) < 1e-10)
                {
                    a = 0;
                }
            }

            var jSigned = direction * jMax;
            AddJerkSegment(tJ, jSigned);   // 1. Jerk up
            AddJerkSegment(tA, 0);         // 2. Constant acceleration
            AddJerkSegment(tJ, -jSigned);  // 3. Jerk down to 0 accel
            AddJerkSegment(tV, 0);         // 4. Constant velocity (cruise)
            AddJerkSegment(tJ, -jSigned);  // 5. Decel jerk down
            AddJerkSegment(tA, 0);         // 6. Constant deceleration
            AddJerkSegment(tJ, jSigned);   // 7. Decel jerk up to rest

            return new TrajectoryPlan
            {
                ProfileType = TrajectoryProfileType.SCurve,
                TotalDistanceMm = direction * distance,
                TotalDurationSec = t,
                PeakVelocityMmPerSec = vPeak,
                PeakAccelerationMmPerSec2 = aPeak,
                PeakJerkMmPerSec3 = jMax,
                ReachedCruiseVelocity = tV > 1e-9,
                Segments = segments,
            };
        }

        private static void ValidateFinitePositive(double value, string paramName)
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Value must be finite and greater than zero.");
            }
        }
    }
}
