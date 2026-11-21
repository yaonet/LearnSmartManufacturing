using LearnSmartManufacturing.Models.Motion;

namespace LearnSmartManufacturing.Services.Motion
{
    /// <summary>
    /// Hardware abstraction for a single servo motion axis.
    /// Production implementations wrap vendor motion cards / EtherCAT drives;
    /// offline development and tests use <see cref="SimulatedMotionAxis"/>.
    /// </summary>
    public interface IMotionAxis
    {
        string Name { get; }
        double SoftNegativeLimitMm { get; }
        double SoftPositiveLimitMm { get; }
        double HardNegativeLimitMm { get; }
        double HardPositiveLimitMm { get; }

        event Action<MotionLogEntry>? LogAdded;

        AxisSnapshot GetSnapshot();

        void SetPulsePerMm(double pulsePerMm);

        bool ServoOn();

        void ServoOff();

        void ResetAlarm();

        void EmergencyStop();

        void Stop();

        bool StartJog(int direction, double velocityMmPerSec, double accelerationMmPerSec2);

        void StopJog();

        Task<bool> HomeAsync(
            HomingMode mode,
            double searchVelocityMmPerSec,
            double creepVelocityMmPerSec,
            double accelerationMmPerSec2,
            CancellationToken cancellationToken = default);

        Task<bool> MoveAbsoluteAsync(
            double targetPositionMm,
            double maxVelocityMmPerSec,
            double maxAccelerationMmPerSec2,
            double maxJerkMmPerSec3,
            TrajectoryProfileType profileType,
            CancellationToken cancellationToken = default);

        Task<bool> MoveRelativeAsync(
            double deltaMm,
            double maxVelocityMmPerSec,
            double maxAccelerationMmPerSec2,
            double maxJerkMmPerSec3,
            TrajectoryProfileType profileType,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Simulation-only extensions for fault injection during offline training and testing.</summary>
    public interface ISimulatedMotionAxis : IMotionAxis
    {
        void InjectFault(MotionAlarmCode alarmCode);
    }
}
