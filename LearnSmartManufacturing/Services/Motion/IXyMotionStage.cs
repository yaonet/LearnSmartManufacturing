using LearnSmartManufacturing.Models.Motion;

namespace LearnSmartManufacturing.Services.Motion
{
    /// <summary>Hardware abstraction for a 2-axis XY gantry stage with synchronized interpolation and PSO output.</summary>
    public interface IXyMotionStage
    {
        double XMinMm { get; }
        double XMaxMm { get; }
        double YMinMm { get; }
        double YMaxMm { get; }

        event Action<MotionLogEntry>? LogAdded;

        XyStageSnapshot GetSnapshot();

        bool ServoOn();

        void ServoOff();

        void ResetAlarm();

        void EmergencyStop();

        void Stop();

        void ClearHistory();

        void RecordCaptureMarker(Point2D position, bool isPso);

        Task<bool> HomeAsync(double speedMmPerSec, CancellationToken cancellationToken = default);

        Task<bool> MoveToAsync(
            Point2D target,
            XyMotionMode mode,
            double feedVelocityMmPerSec,
            double maxAccelMmPerSec2,
            double maxJerkMmPerSec3,
            TrajectoryProfileType profileType,
            double arcRadiusMm = 0,
            bool psoEnabled = false,
            double psoIntervalMm = 15.0,
            CancellationToken cancellationToken = default);
    }
}
