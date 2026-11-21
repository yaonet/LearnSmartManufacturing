namespace LearnSmartManufacturing.Models.Motion
{
    /// <summary>PLCopen / CiA 402 single-axis state machine.</summary>
    public enum AxisState
    {
        Disabled,
        Standstill,
        Homing,
        DiscreteMotion,
        ContinuousMotion,
        Stopping,
        ErrorStop,
    }

    public enum TrajectoryProfileType
    {
        Trapezoidal,
        SCurve,
    }

    public enum MotionAlarmCode
    {
        None,
        EmergencyStop,
        PositiveLimit,
        NegativeLimit,
        FollowingErrorExceeded,
        ServoFault,
    }

    public enum HomingMode
    {
        NegativeLimitThenOrigin,
        OriginSwitchOnly,
    }

    public readonly record struct MotionProfileParameters(
        double DistanceMm,
        double MaxVelocityMmPerSec,
        double MaxAccelerationMmPerSec2,
        double MaxJerkMmPerSec3,
        TrajectoryProfileType ProfileType);

    public readonly record struct TrajectorySegment(
        double StartTimeSec,
        double DurationSec,
        double StartPositionMm,
        double StartVelocityMmPerSec,
        double StartAccelerationMmPerSec2,
        double JerkMmPerSec3)
    {
        public double EndTimeSec => StartTimeSec + DurationSec;

        public TrajectorySample EvaluateLocal(double localTimeSec)
        {
            var t = Math.Clamp(localTimeSec, 0, DurationSec);
            var t2 = t * t;
            var t3 = t2 * t;
            var pos = StartPositionMm + StartVelocityMmPerSec * t + 0.5 * StartAccelerationMmPerSec2 * t2 + (1.0 / 6.0) * JerkMmPerSec3 * t3;
            var vel = StartVelocityMmPerSec + StartAccelerationMmPerSec2 * t + 0.5 * JerkMmPerSec3 * t2;
            var acc = StartAccelerationMmPerSec2 + JerkMmPerSec3 * t;
            return new TrajectorySample(StartTimeSec + t, pos, vel, acc, JerkMmPerSec3);
        }
    }

    public readonly record struct TrajectorySample(
        double TimeSec,
        double PositionMm,
        double VelocityMmPerSec,
        double AccelerationMmPerSec2,
        double JerkMmPerSec3);

    public sealed record TrajectoryPlan
    {
        public TrajectoryProfileType ProfileType { get; init; }
        public double TotalDistanceMm { get; init; }
        public double TotalDurationSec { get; init; }
        public double PeakVelocityMmPerSec { get; init; }
        public double PeakAccelerationMmPerSec2 { get; init; }
        public double PeakJerkMmPerSec3 { get; init; }
        public bool ReachedCruiseVelocity { get; init; }
        public IReadOnlyList<TrajectorySegment> Segments { get; init; } = [];

        public TrajectorySample Evaluate(double timeSec)
        {
            if (Segments.Count == 0 || timeSec <= 0)
            {
                return new TrajectorySample(0, 0, 0, 0, 0);
            }

            if (timeSec >= TotalDurationSec)
            {
                return new TrajectorySample(TotalDurationSec, TotalDistanceMm, 0, 0, 0);
            }

            for (var i = 0; i < Segments.Count; i++)
            {
                var seg = Segments[i];
                if (timeSec <= seg.EndTimeSec || i == Segments.Count - 1)
                {
                    return seg.EvaluateLocal(timeSec - seg.StartTimeSec);
                }
            }

            return new TrajectorySample(TotalDurationSec, TotalDistanceMm, 0, 0, 0);
        }

        public IReadOnlyList<TrajectorySample> SampleSeries(int pointCount)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(pointCount, 2);

            var samples = new TrajectorySample[pointCount];
            if (TotalDurationSec <= 0)
            {
                for (var i = 0; i < pointCount; i++)
                {
                    samples[i] = new TrajectorySample(0, 0, 0, 0, 0);
                }

                return samples;
            }

            for (var i = 0; i < pointCount; i++)
            {
                var t = TotalDurationSec * i / (pointCount - 1);
                samples[i] = Evaluate(t);
            }

            return samples;
        }
    }

    public readonly record struct AxisSnapshot(
        AxisState State,
        bool IsServoOn,
        bool IsHomed,
        bool IsInPosition,
        bool IsMoving,
        bool PositiveLimit,
        bool NegativeLimit,
        bool OriginSensor,
        MotionAlarmCode AlarmCode,
        string AlarmMessage,
        double CommandPositionMm,
        double ActualPositionMm,
        double VelocityMmPerSec,
        double AccelerationMmPerSec2,
        double JerkMmPerSec3,
        double PulsePerMm,
        double ElapsedMoveTimeSec,
        double TargetPositionMm,
        TrajectoryPlan? ActivePlan)
    {
        public double FollowingErrorMm => CommandPositionMm - ActualPositionMm;
        public long CommandPulse => (long)Math.Round(CommandPositionMm * PulsePerMm);
        public long ActualPulse => (long)Math.Round(ActualPositionMm * PulsePerMm);
    }

    public sealed record MotionLogEntry(DateTime Timestamp, string Level, string Message)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
    }
}
