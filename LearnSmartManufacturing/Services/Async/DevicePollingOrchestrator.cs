using System.Diagnostics;
using System.IO;
using LearnSmartManufacturing.Models.Async;

namespace LearnSmartManufacturing.Services.Async
{
    /// <summary>
    /// Runs multi-device parallel I/O polling with <see cref="SemaphoreSlim"/> concurrency throttling,
    /// <see cref="Task.WhenAll"/> coordination, per-device linked timeouts (<see cref="CancellationTokenSource.CreateLinkedTokenSource(CancellationToken)"/>),
    /// and isolated exception handling.
    /// </summary>
    public sealed class DevicePollingOrchestrator
    {
        private readonly Lock _sync = new();
        private DeviceExecutionSnapshot[] _devices = [];
        private Stopwatch? _runStopwatch;
        private bool _isRunning;
        private int _maxConcurrency = 3;
        private int _currentRunning;
        private int _peakConcurrency;
        private double _finalElapsedSec;
        private double _theoreticalSerialSec;

        public event Action<AsyncLogEntry>? LogAdded;

        public void InitializeIdle(IReadOnlyList<DeviceSpec> specs, int maxConcurrency)
        {
            lock (_sync)
            {
                _maxConcurrency = maxConcurrency;
                _currentRunning = 0;
                _peakConcurrency = 0;
                _finalElapsedSec = 0;
                _theoreticalSerialSec = specs.Sum(s => s.SimulatedDurationMs) / 1000.0;
                _devices = specs.Select(s => new DeviceExecutionSnapshot(
                    s.Id, s.Name, s.Category, DeviceTaskStatus.Idle, -1, -1, -1, 0,
                    $"{s.SimulatedDurationMs} ms")).ToArray();
            }
        }

        public OrchestrationSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                var elapsed = _isRunning && _runStopwatch is not null
                    ? _runStopwatch.Elapsed.TotalSeconds
                    : _finalElapsedSec;

                return new OrchestrationSnapshot(
                    _isRunning,
                    _maxConcurrency,
                    _currentRunning,
                    _peakConcurrency,
                    elapsed,
                    _theoreticalSerialSec,
                    _devices.ToArray());
            }
        }

        public async Task<OrchestrationResult> RunBatchAsync(
            IReadOnlyList<DeviceSpec> specs,
            int maxConcurrency,
            int deviceTimeoutMs,
            CancellationToken globalCancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(deviceTimeoutMs, 50);

            var sw = Stopwatch.StartNew();
            var serialSec = specs.Sum(s => s.SimulatedDurationMs) / 1000.0;

            lock (_sync)
            {
                _isRunning = true;
                _runStopwatch = sw;
                _maxConcurrency = maxConcurrency;
                _currentRunning = 0;
                _peakConcurrency = 0;
                _finalElapsedSec = 0;
                _theoreticalSerialSec = serialSec;
                _devices = specs.Select(s => new DeviceExecutionSnapshot(
                    s.Id, s.Name, s.Category, DeviceTaskStatus.Queued, 0, -1, -1, 0, "排队等待信号量")).ToArray();
            }

            Emit("Info", $"开始并行巡检 {specs.Count} 台设备：SemaphoreSlim 最大并发={maxConcurrency}，单设备超时={deviceTimeoutMs} ms。");

            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = new Task[specs.Count];
            for (var i = 0; i < specs.Count; i++)
            {
                var index = i;
                var spec = specs[i];
                tasks[i] = Task.Run(() => ExecuteSingleDeviceAsync(index, spec, semaphore, deviceTimeoutMs, sw, globalCancellationToken), CancellationToken.None);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            sw.Stop();

            int succeeded = 0, timedOut = 0, faulted = 0, canceled = 0, peak;
            lock (_sync)
            {
                _isRunning = false;
                _finalElapsedSec = sw.Elapsed.TotalSeconds;
                peak = _peakConcurrency;
                foreach (var d in _devices)
                {
                    switch (d.Status)
                    {
                        case DeviceTaskStatus.Succeeded: succeeded++; break;
                        case DeviceTaskStatus.TimedOut: timedOut++; break;
                        case DeviceTaskStatus.Faulted: faulted++; break;
                        case DeviceTaskStatus.Canceled: canceled++; break;
                    }
                }
            }

            var result = new OrchestrationResult(succeeded, timedOut, faulted, canceled, peak, sw.Elapsed.TotalSeconds, serialSec);
            Emit("Info",
                $"批次结束：成功 {succeeded} / 超时 {timedOut} / 异常 {faulted} / 取消 {canceled}，峰值并发 {peak}，实测 {result.ElapsedSec:F2}s (串行 {serialSec:F2}s，加速 {result.Speedup:F2}×)。");
            return result;
        }

        private async Task ExecuteSingleDeviceAsync(
            int index,
            DeviceSpec spec,
            SemaphoreSlim semaphore,
            int deviceTimeoutMs,
            Stopwatch sw,
            CancellationToken globalCt)
        {
            var acquired = false;
            try
            {
                await semaphore.WaitAsync(globalCt).ConfigureAwait(false);
                acquired = true;

                var startSec = sw.Elapsed.TotalSeconds;
                var tid = Environment.CurrentManagedThreadId;

                lock (_sync)
                {
                    _currentRunning++;
                    if (_currentRunning > _peakConcurrency)
                    {
                        _peakConcurrency = _currentRunning;
                    }

                    _devices[index] = _devices[index] with
                    {
                        Status = DeviceTaskStatus.Running,
                        StartedAtSec = startSec,
                        WorkerThreadId = tid,
                        Detail = $"采集中 (T{tid:D2})",
                    };
                }

                Emit("Info", $"{spec.Name} 获取信号量开始采集 (排队 {startSec * 1000:F0} ms)。");

                // Link global cancellation with per-device timeout.
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(globalCt);
                linkedCts.CancelAfter(deviceTimeoutMs);

                var targetMs = spec.InjectTimeout
                    ? Math.Max(spec.SimulatedDurationMs, deviceTimeoutMs + 350)
                    : spec.SimulatedDurationMs;

                if (spec.InjectFault)
                {
                    await Task.Delay(Math.Max(60, (int)(spec.SimulatedDurationMs * 0.55)), linkedCts.Token).ConfigureAwait(false);
                    throw new IOException("Modbus/TCP CRC 校验失败 (0x04)");
                }

                await Task.Delay(targetMs, linkedCts.Token).ConfigureAwait(false);

                var finishSec = sw.Elapsed.TotalSeconds;
                var durMs = (finishSec - startSec) * 1000.0;
                lock (_sync)
                {
                    _devices[index] = _devices[index] with
                    {
                        Status = DeviceTaskStatus.Succeeded,
                        FinishedAtSec = finishSec,
                        Detail = $"OK · {durMs:F0} ms",
                    };
                }

                Emit("Info", $"{spec.Name} 采集成功 ({durMs:F0} ms)。");
            }
            catch (OperationCanceledException) when (globalCt.IsCancellationRequested)
            {
                var finishSec = sw.Elapsed.TotalSeconds;
                lock (_sync)
                {
                    var cur = _devices[index];
                    _devices[index] = cur with
                    {
                        Status = DeviceTaskStatus.Canceled,
                        StartedAtSec = cur.StartedAtSec < 0 ? finishSec : cur.StartedAtSec,
                        FinishedAtSec = finishSec,
                        Detail = "全局取消",
                    };
                }

                Emit("Warn", $"{spec.Name} 已响应全局 CancellationToken 优雅取消。");
            }
            catch (OperationCanceledException)
            {
                var finishSec = sw.Elapsed.TotalSeconds;
                lock (_sync)
                {
                    _devices[index] = _devices[index] with
                    {
                        Status = DeviceTaskStatus.TimedOut,
                        FinishedAtSec = finishSec,
                        Detail = $"超时 (>{deviceTimeoutMs}ms)",
                    };
                }

                Emit("Warn", $"{spec.Name} 触发单设备超时保护 (>{deviceTimeoutMs} ms)，不影响其余设备。");
            }
            catch (Exception ex)
            {
                var finishSec = sw.Elapsed.TotalSeconds;
                lock (_sync)
                {
                    _devices[index] = _devices[index] with
                    {
                        Status = DeviceTaskStatus.Faulted,
                        FinishedAtSec = finishSec,
                        Detail = ex.Message,
                    };
                }

                Emit("Error", $"{spec.Name} 发生通信异常（已隔离捕获）：{ex.Message}");
            }
            finally
            {
                if (acquired)
                {
                    lock (_sync)
                    {
                        _currentRunning = Math.Max(0, _currentRunning - 1);
                    }

                    semaphore.Release();
                }
            }
        }

        private void Emit(string level, string message) =>
            LogAdded?.Invoke(new AsyncLogEntry(DateTime.Now, Environment.CurrentManagedThreadId, level, message));
    }
}
