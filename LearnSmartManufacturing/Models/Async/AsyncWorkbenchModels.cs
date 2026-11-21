using System.Threading.Channels;

namespace LearnSmartManufacturing.Models.Async
{
    public sealed record AsyncLogEntry(DateTime Timestamp, int ThreadId, string Level, string Message)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
        public string ThreadText => $"T{ThreadId:D2}";
    }

    public readonly record struct FrameItem(long Sequence, long CreatedTimestampTicks);

    public readonly record struct PipelineConfig(
        double ProduceRateHz,
        int ProcessTimeMs,
        int Capacity,
        int WorkerCount,
        BoundedChannelFullMode FullMode);

    public readonly record struct WorkerSnapshot(
        int WorkerId,
        bool IsBusy,
        long CurrentFrameSeq,
        double Progress01,
        int ThreadId);

    public sealed record PipelineSnapshot(
        bool IsRunning,
        BoundedChannelFullMode FullMode,
        int Capacity,
        int CurrentDepth,
        long ProducedCount,
        long ConsumedCount,
        long DroppedCount,
        bool IsBackpressured,
        double AverageLatencyMs,
        double MeasuredThroughputFps,
        IReadOnlyList<long> QueuedFrameIds,
        IReadOnlyList<WorkerSnapshot> Workers,
        IReadOnlyList<int> DepthHistory);

    public enum DeviceTaskStatus
    {
        Idle,
        Queued,
        Running,
        Succeeded,
        TimedOut,
        Faulted,
        Canceled,
    }

    public sealed record DeviceSpec(
        int Id,
        string Name,
        string Category,
        int SimulatedDurationMs,
        bool InjectTimeout = false,
        bool InjectFault = false);

    public readonly record struct DeviceExecutionSnapshot(
        int Id,
        string Name,
        string Category,
        DeviceTaskStatus Status,
        double QueuedAtSec,
        double StartedAtSec,
        double FinishedAtSec,
        int WorkerThreadId,
        string Detail);

    public sealed record OrchestrationSnapshot(
        bool IsRunning,
        int MaxConcurrency,
        int CurrentRunning,
        int PeakConcurrency,
        double ElapsedSec,
        double TheoreticalSerialSec,
        IReadOnlyList<DeviceExecutionSnapshot> Devices);

    public sealed record OrchestrationResult(
        int SucceededCount,
        int TimedOutCount,
        int FaultedCount,
        int CanceledCount,
        int PeakConcurrency,
        double ElapsedSec,
        double TheoreticalSerialSec)
    {
        public double Speedup => ElapsedSec > 1e-6 ? TheoreticalSerialSec / ElapsedSec : 1.0;
    }
}
