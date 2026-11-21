using System.Diagnostics;
using System.Threading.Channels;
using LearnSmartManufacturing.Models.Async;

namespace LearnSmartManufacturing.Services.Async
{
    /// <summary>
    /// Real <see cref="Channel{T}"/> bounded producer-consumer simulator demonstrating backpressure
    /// (<see cref="BoundedChannelFullMode.Wait"/>, <see cref="BoundedChannelFullMode.DropOldest"/>,
    /// <see cref="BoundedChannelFullMode.DropWrite"/>) with live rate adjustment and multi-worker consumption.
    /// </summary>
    public sealed class ChannelPipelineSimulator : IDisposable
    {
        private const int MaxDepthHistory = 72;

        private readonly Lock _sync = new();
        private readonly List<long> _queuedIds = [];
        private readonly Queue<double> _recentConsumeTimestamps = new();
        private readonly List<int> _depthHistory = [];

        private WorkerSnapshot[] _workers = [];
        private CancellationTokenSource? _runCts;
        private Task? _runnerTask;
        private PipelineConfig _config = new(60, 45, 12, 2, BoundedChannelFullMode.DropOldest);

        private volatile bool _isRunning;
        private volatile bool _isBackpressured;
        private double _liveProduceRateHz = 60;
        private int _liveProcessTimeMs = 45;

        private long _producedCount;
        private long _consumedCount;
        private long _droppedCount;
        private double _avgLatencyMs;
        private bool _disposed;

        public event Action<AsyncLogEntry>? LogAdded;

        public bool IsRunning => _isRunning;

        public void Start(PipelineConfig config)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stop();

            if (config.Capacity < 1 || config.WorkerCount < 1 || config.ProduceRateHz <= 0 || config.ProcessTimeMs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(config), "Pipeline configuration values must be positive.");
            }

            var cts = new CancellationTokenSource();
            lock (_sync)
            {
                _config = config;
                _liveProduceRateHz = config.ProduceRateHz;
                _liveProcessTimeMs = config.ProcessTimeMs;
                _producedCount = 0;
                _consumedCount = 0;
                _droppedCount = 0;
                _avgLatencyMs = 0;
                _isBackpressured = false;
                _queuedIds.Clear();
                _recentConsumeTimestamps.Clear();
                _depthHistory.Clear();
                _workers = new WorkerSnapshot[config.WorkerCount];
                for (var i = 0; i < config.WorkerCount; i++)
                {
                    _workers[i] = new WorkerSnapshot(i + 1, false, 0, 0, 0);
                }

                _runCts = cts;
                _isRunning = true;
            }

            Emit("Info", $"启动 Channel 流水线：模式={config.FullMode}，容量={config.Capacity}，Worker={config.WorkerCount}，采集={config.ProduceRateHz:F0} Hz，处理={config.ProcessTimeMs} ms。");
            _runnerTask = Task.Run(() => RunPipelineAsync(config, cts.Token), CancellationToken.None);
        }

        public void UpdateLiveRates(double produceRateHz, int processTimeMs)
        {
            if (produceRateHz > 0)
            {
                Volatile.Write(ref _liveProduceRateHz, produceRateHz);
            }

            if (processTimeMs >= 1)
            {
                Volatile.Write(ref _liveProcessTimeMs, processTimeMs);
            }
        }

        public void Stop()
        {
            CancellationTokenSource? cts;
            Task? task;
            bool wasRunning;

            lock (_sync)
            {
                wasRunning = _isRunning;
                _isRunning = false;
                _isBackpressured = false;
                cts = _runCts;
                task = _runnerTask;
                _runCts = null;
                _runnerTask = null;
            }

            if (cts is not null)
            {
                cts.Cancel();
                try
                {
                    task?.Wait(TimeSpan.FromMilliseconds(300));
                }
                catch
                {
                    // Ignore cancellation faults during stop.
                }

                cts.Dispose();
            }

            if (wasRunning)
            {
                Emit("Info", $"Channel 流水线已停止（生产 {Interlocked.Read(ref _producedCount)}，消费 {Interlocked.Read(ref _consumedCount)}，丢弃 {Interlocked.Read(ref _droppedCount)}）。");
            }
        }

        public PipelineSnapshot GetSnapshot()
        {
            var nowSec = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            lock (_sync)
            {
                while (_recentConsumeTimestamps.Count > 0 && nowSec - _recentConsumeTimestamps.Peek() > 1.0)
                {
                    _recentConsumeTimestamps.Dequeue();
                }

                var depth = _queuedIds.Count;
                if (_isRunning)
                {
                    _depthHistory.Add(depth);
                    if (_depthHistory.Count > MaxDepthHistory)
                    {
                        _depthHistory.RemoveAt(0);
                    }
                }

                return new PipelineSnapshot(
                    _isRunning,
                    _config.FullMode,
                    _config.Capacity,
                    depth,
                    Interlocked.Read(ref _producedCount),
                    Interlocked.Read(ref _consumedCount),
                    Interlocked.Read(ref _droppedCount),
                    _isBackpressured,
                    _avgLatencyMs,
                    _recentConsumeTimestamps.Count,
                    _queuedIds.ToArray(),
                    _workers.ToArray(),
                    _depthHistory.ToArray());
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }

        private async Task RunPipelineAsync(PipelineConfig config, CancellationToken ct)
        {
            var options = new BoundedChannelOptions(config.Capacity)
            {
                FullMode = config.FullMode,
                SingleWriter = true,
                SingleReader = config.WorkerCount == 1,
                AllowSynchronousContinuations = false,
            };

            var channel = Channel.CreateBounded<FrameItem>(options, OnItemDropped);

            var workers = new Task[config.WorkerCount];
            for (var i = 0; i < config.WorkerCount; i++)
            {
                var workerIndex = i;
                workers[i] = Task.Run(() => RunWorkerAsync(workerIndex, channel.Reader, ct), CancellationToken.None);
            }

            try
            {
                await RunProducerAsync(channel.Writer, config.FullMode, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on stop.
            }
            finally
            {
                channel.Writer.TryComplete();
                try
                {
                    await Task.WhenAll(workers).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected on stop.
                }
            }
        }

        private async Task RunProducerAsync(ChannelWriter<FrameItem> writer, BoundedChannelFullMode mode, CancellationToken ct)
        {
            long seq = 0;
            while (!ct.IsCancellationRequested)
            {
                var rateHz = Math.Max(1.0, Volatile.Read(ref _liveProduceRateHz));
                var intervalMs = Math.Max(4, (int)Math.Round(1000.0 / rateHz));
                await Task.Delay(intervalMs, ct).ConfigureAwait(false);

                seq++;
                var frame = new FrameItem(seq, Stopwatch.GetTimestamp());
                Interlocked.Increment(ref _producedCount);

                if (mode == BoundedChannelFullMode.Wait)
                {
                    while (!ct.IsCancellationRequested)
                    {
                        lock (_sync)
                        {
                            _queuedIds.Add(frame.Sequence);
                        }

                        if (writer.TryWrite(frame))
                        {
                            _isBackpressured = false;
                            break;
                        }

                        lock (_sync)
                        {
                            _queuedIds.Remove(frame.Sequence);
                        }

                        _isBackpressured = true;
                        if (!await writer.WaitToWriteAsync(ct).ConfigureAwait(false))
                        {
                            return;
                        }
                    }
                }
                else
                {
                    lock (_sync)
                    {
                        _queuedIds.Add(frame.Sequence);
                    }

                    writer.TryWrite(frame);
                }
            }
        }

        private async Task RunWorkerAsync(int workerIndex, ChannelReader<FrameItem> reader, CancellationToken ct)
        {
            try
            {
                await foreach (var frame in reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    var tid = Environment.CurrentManagedThreadId;
                    lock (_sync)
                    {
                        _queuedIds.Remove(frame.Sequence);
                        _workers[workerIndex] = new WorkerSnapshot(workerIndex + 1, true, frame.Sequence, 0, tid);
                    }

                    var totalMs = Math.Max(4, Volatile.Read(ref _liveProcessTimeMs));
                    const int sliceMs = 15;
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < totalMs)
                    {
                        ct.ThrowIfCancellationRequested();
                        var remaining = totalMs - (int)sw.ElapsedMilliseconds;
                        if (remaining <= 0)
                        {
                            break;
                        }

                        await Task.Delay(Math.Min(sliceMs, remaining), ct).ConfigureAwait(false);
                        var progress = Math.Clamp(sw.Elapsed.TotalMilliseconds / totalMs, 0, 1);
                        lock (_sync)
                        {
                            _workers[workerIndex] = new WorkerSnapshot(workerIndex + 1, true, frame.Sequence, progress, Environment.CurrentManagedThreadId);
                        }
                    }

                    var latencyMs = (Stopwatch.GetTimestamp() - frame.CreatedTimestampTicks) * 1000.0 / Stopwatch.Frequency;
                    var nowSec = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                    Interlocked.Increment(ref _consumedCount);

                    lock (_sync)
                    {
                        _avgLatencyMs = _avgLatencyMs <= 0 ? latencyMs : _avgLatencyMs * 0.85 + latencyMs * 0.15;
                        _recentConsumeTimestamps.Enqueue(nowSec);
                        _workers[workerIndex] = new WorkerSnapshot(workerIndex + 1, false, frame.Sequence, 1, Environment.CurrentManagedThreadId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    if (workerIndex < _workers.Length)
                    {
                        _workers[workerIndex] = new WorkerSnapshot(workerIndex + 1, false, 0, 0, 0);
                    }
                }
            }
        }

        private void OnItemDropped(FrameItem dropped)
        {
            Interlocked.Increment(ref _droppedCount);
            lock (_sync)
            {
                _queuedIds.Remove(dropped.Sequence);
            }
        }

        private void Emit(string level, string message) =>
            LogAdded?.Invoke(new AsyncLogEntry(DateTime.Now, Environment.CurrentManagedThreadId, level, message));
    }
}
