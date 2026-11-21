using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.Async;
using LearnSmartManufacturing.Services.Async;

namespace LearnSmartManufacturing.ViewModels
{
    public record ChannelModeOption(string Name, BoundedChannelFullMode Value);

    public partial class AsyncProgrammingViewModel : ObservableObject, IDisposable
    {
        private const int MaxLogEntries = 120;

        private readonly ChannelPipelineSimulator _pipeline = new();
        private readonly DevicePollingOrchestrator _orchestrator = new();
        private readonly ConcurrentQueue<AsyncLogEntry> _pendingTab1Logs = new();
        private readonly ConcurrentQueue<AsyncLogEntry> _pendingTab2Logs = new();
        private readonly DispatcherTimer? _uiTimer;

        private CancellationTokenSource? _responsivenessCts;
        private CancellationTokenSource? _batchCts;
        private bool _disposed;

        public AsyncProgrammingViewModel()
        {
            FullModeOptions =
            [
                new ChannelModeOption("DropOldest · 丢最旧帧（视觉实时预览）", BoundedChannelFullMode.DropOldest),
                new ChannelModeOption("Wait · 背压限速（关键数据不丢帧）", BoundedChannelFullMode.Wait),
                new ChannelModeOption("DropWrite · 满载拒收新帧", BoundedChannelFullMode.DropWrite),
            ];
            SelectedFullMode = FullModeOptions[0];

            _pipeline.LogAdded += e => _pendingTab1Logs.Enqueue(e);
            _orchestrator.LogAdded += e => _pendingTab2Logs.Enqueue(e);

            _orchestrator.InitializeIdle(BuildDeviceSpecs(), MaxConcurrency);
            PipelineSnapshot = _pipeline.GetSnapshot();
            OrchestrationSnapshot = _orchestrator.GetSnapshot();

            if (Application.Current is not null)
            {
                _uiTimer = new DispatcherTimer(DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(25),
                };
                _uiTimer.Tick += (_, _) => RefreshSnapshots();
                _uiTimer.Start();
            }
        }

        public string Heading => "异步编程";

        public IReadOnlyList<ChannelModeOption> FullModeOptions { get; }
        public ObservableCollection<AsyncLogEntry> Tab1Logs { get; } = [];
        public ObservableCollection<AsyncLogEntry> Tab2Logs { get; } = [];

        // === Tab 1: UI Responsiveness & Deadlock ===

        [ObservableProperty]
        public partial bool IsResponsivenessBusy { get; private set; }

        [ObservableProperty]
        public partial int ResponsivenessProgress { get; private set; }

        [ObservableProperty]
        public partial string ResponsivenessStatusText { get; private set; } = "就绪：可对比同步阻塞、async/await 非阻塞与 Sync-over-Async 死锁";

        // === Tab 1: Channel<T> Pipeline ===

        [ObservableProperty]
        public partial ChannelModeOption SelectedFullMode { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ProduceRateText))]
        public partial double ProduceRateHz { get; set; } = 60;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ProcessTimeText))]
        public partial int ProcessTimeMs { get; set; } = 45;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CapacityText))]
        public partial int ChannelCapacity { get; set; } = 12;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WorkerCountText))]
        public partial int WorkerCount { get; set; } = 2;

        [ObservableProperty]
        public partial bool IsPipelineRunning { get; private set; }

        [ObservableProperty]
        public partial PipelineSnapshot PipelineSnapshot { get; private set; }

        public string ProduceRateText => $"{ProduceRateHz:F0} 帧/s";
        public string ProcessTimeText => $"{ProcessTimeMs} ms/帧";
        public string CapacityText => $"{ChannelCapacity} 槽";
        public string WorkerCountText => $"{WorkerCount} 路";

        public string ProducedText => $"{PipelineSnapshot.ProducedCount:N0}";
        public string ConsumedText => $"{PipelineSnapshot.ConsumedCount:N0}";
        public string ThroughputSubText => $"实时吞吐 {PipelineSnapshot.MeasuredThroughputFps:F0} fps";
        public string DroppedText => $"{PipelineSnapshot.DroppedCount:N0}";
        public string BackpressureSubText => PipelineSnapshot.IsBackpressured
            ? "⏸ 生产者正在背压等待"
            : $"策略 {PipelineSnapshot.FullMode}";
        public string QueueDepthText => $"{PipelineSnapshot.CurrentDepth} / {PipelineSnapshot.Capacity}";
        public string LatencySubText => $"平均延迟 {PipelineSnapshot.AverageLatencyMs:F0} ms";

        // === Tab 2: Multi-Device SemaphoreSlim Orchestrator ===

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxConcurrencyText))]
        public partial int MaxConcurrency { get; set; } = 3;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DeviceTimeoutText))]
        public partial int DeviceTimeoutMs { get; set; } = 650;

        [ObservableProperty]
        public partial bool InjectTimeoutOnTmp01 { get; set; } = true;

        [ObservableProperty]
        public partial bool InjectFaultOnBar01 { get; set; }

        [ObservableProperty]
        public partial bool IsBatchRunning { get; private set; }

        [ObservableProperty]
        public partial OrchestrationSnapshot OrchestrationSnapshot { get; private set; }

        public string MaxConcurrencyText => $"{MaxConcurrency} 并发";
        public string DeviceTimeoutText => $"{DeviceTimeoutMs} ms";

        public string ConcurrencyCardText => $"{OrchestrationSnapshot.CurrentRunning} / {OrchestrationSnapshot.MaxConcurrency}";
        public string PeakConcurrencySubText => $"峰值并发 {OrchestrationSnapshot.PeakConcurrency}";
        public string ElapsedCardText => $"{OrchestrationSnapshot.ElapsedSec:F2} s";
        public string SerialSubText => $"串行理论 {OrchestrationSnapshot.TheoreticalSerialSec:F2} s";
        public string SpeedupCardText => OrchestrationSnapshot.ElapsedSec > 0.05
            ? $"{OrchestrationSnapshot.TheoreticalSerialSec / OrchestrationSnapshot.ElapsedSec:F2}×"
            : "—";
        public string BatchOutcomeText
        {
            get
            {
                var ok = OrchestrationSnapshot.Devices.Count(d => d.Status == DeviceTaskStatus.Succeeded);
                var fail = OrchestrationSnapshot.Devices.Count(d => d.Status is DeviceTaskStatus.TimedOut or DeviceTaskStatus.Faulted or DeviceTaskStatus.Canceled);
                return $"成功 {ok} · 异常/超时 {fail}";
            }
        }

        // === Tab 1 Commands ===

        [RelayCommand]
        private void RunSyncBlock()
        {
            var uiTid = Environment.CurrentManagedThreadId;
            EnqueueTab1("Warn", $"在 UI 线程 [T{uiTid:D2}] 执行 Thread.Sleep(1200) —— 观察顶部心跳条完全冻结！");
            ResponsivenessStatusText = "同步阻塞中 (UI 线程冻结)...";
            ResponsivenessProgress = 0;

            var sw = Stopwatch.StartNew();
            Thread.Sleep(1200);
            sw.Stop();

            ResponsivenessProgress = 100;
            ResponsivenessStatusText = $"同步阻塞结束：UI 主线程冻结 {sw.ElapsedMilliseconds} ms";
            EnqueueTab1("Error", $"Thread.Sleep 结束，UI 线程被锁死 {sw.ElapsedMilliseconds} ms，期间无法响应任何点击或急停！");
        }

        [RelayCommand]
        private async Task RunAsyncNonBlockingAsync()
        {
            if (IsResponsivenessBusy)
            {
                return;
            }

            CancelResponsiveness();
            _responsivenessCts = new CancellationTokenSource();
            var ct = _responsivenessCts.Token;

            IsResponsivenessBusy = true;
            ResponsivenessProgress = 0;
            var startTid = Environment.CurrentManagedThreadId;
            ResponsivenessStatusText = "异步采集中（UI 线程保持 60 FPS）...";
            EnqueueTab1("Info", $"在 UI 线程 [T{startTid:D2}] 启动 async/await 异步任务，通过 IProgress<int> 回传进度。");

            var progress = new Progress<int>(p => ResponsivenessProgress = p);
            var sw = Stopwatch.StartNew();
            try
            {
                await ExecuteAsyncWorkAsync(progress, ct);
                sw.Stop();
                var resumeTid = Environment.CurrentManagedThreadId;
                ResponsivenessStatusText = $"异步完成 ({sw.ElapsedMilliseconds} ms)，自动回到 UI 线程 [T{resumeTid:D2}]";
                EnqueueTab1("Info", $"异步任务完成 ({sw.ElapsedMilliseconds} ms)，await 续体自动回到 UI 线程 [T{resumeTid:D2}]，期间界面全程流畅。");
            }
            catch (OperationCanceledException)
            {
                ResponsivenessStatusText = "异步任务已响应 CancellationToken 安全取消";
                EnqueueTab1("Warn", "异步任务已通过 CancellationToken 取消，未造成界面卡顿。");
            }
            finally
            {
                IsResponsivenessBusy = false;
            }
        }

        [RelayCommand]
        private void CancelResponsiveness()
        {
            if (_responsivenessCts is not null)
            {
                _responsivenessCts.Cancel();
                _responsivenessCts.Dispose();
                _responsivenessCts = null;
            }
        }

        [RelayCommand]
        private void RunDeadlockDemo()
        {
            var uiTid = Environment.CurrentManagedThreadId;
            var hasSyncCtx = SynchronizationContext.Current is not null;
            EnqueueTab1("Warn",
                $"死锁实验：在 [T{uiTid:D2}] 同步调用 Task.Wait(900ms) 等待未加 ConfigureAwait(false) 的 100ms 异步方法 (SyncCtx={(hasSyncCtx ? "存在" : "无")})...");

            var sw = Stopwatch.StartNew();
            var completed = SimulatedLibraryWithoutConfigureAwaitAsync().Wait(TimeSpan.FromMilliseconds(900));
            sw.Stop();

            if (!completed)
            {
                ResponsivenessStatusText = $"死锁复现！100ms 任务因 UI 线程被 .Wait() 锁死，{sw.ElapsedMilliseconds}ms 超时未完成";
                EnqueueTab1("Error",
                    $"死锁验证成功 ({sw.ElapsedMilliseconds} ms 超时脱困)：await 默认捕获 DispatcherSynchronizationContext，续体排队等 UI 线程，而 UI 线程在 .Wait() 死等任务，形成循环等待！");
            }
            else
            {
                ResponsivenessStatusText = $"无 SynchronizationContext 环境，未发生死锁 ({sw.ElapsedMilliseconds} ms)";
                EnqueueTab1("Info", $"当前线程无 SynchronizationContext，任务在 {sw.ElapsedMilliseconds} ms 完成（WPF UI 线程下会死锁）。");
            }
        }

        [RelayCommand]
        private void RunConfigureAwaitFix()
        {
            var uiTid = Environment.CurrentManagedThreadId;
            EnqueueTab1("Info", $"对照实验：在 [T{uiTid:D2}] 调用带 ConfigureAwait(false) 的 100ms 库方法，观察续体在线程池完成而不死锁...");

            var sw = Stopwatch.StartNew();
            var (completed, continuationTid) = RunWithConfigureAwaitFalseHelper();
            sw.Stop();

            if (completed)
            {
                ResponsivenessStatusText = $"ConfigureAwait(false) 未死锁：耗时 {sw.ElapsedMilliseconds} ms，续体运行于线程池 [T{continuationTid:D2}]";
                EnqueueTab1("Info",
                    $"ConfigureAwait(false) 破局成功 ({sw.ElapsedMilliseconds} ms)：续体在线程池 [T{continuationTid:D2}] 完成而无需抢占 UI 线程。注意：工程最佳仍是全链路 async/await，不在 UI 线程调 .Wait()/.Result！");
            }
        }

        [RelayCommand]
        private void StartPipeline()
        {
            var cfg = new PipelineConfig(ProduceRateHz, ProcessTimeMs, ChannelCapacity, WorkerCount, SelectedFullMode.Value);
            _pipeline.Start(cfg);
            IsPipelineRunning = _pipeline.IsRunning;
            RefreshSnapshots();
        }

        [RelayCommand]
        private void StopPipeline()
        {
            _pipeline.Stop();
            IsPipelineRunning = _pipeline.IsRunning;
            RefreshSnapshots();
        }

        [RelayCommand]
        private void ClearTab1Logs() => Tab1Logs.Clear();

        // === Tab 2 Commands ===

        [RelayCommand]
        private async Task RunDeviceBatchAsync()
        {
            if (IsBatchRunning)
            {
                return;
            }

            CancelDeviceBatch();
            _batchCts = new CancellationTokenSource();
            var ct = _batchCts.Token;

            IsBatchRunning = true;
            try
            {
                var specs = BuildDeviceSpecs();
                await _orchestrator.RunBatchAsync(specs, MaxConcurrency, DeviceTimeoutMs, ct);
            }
            finally
            {
                IsBatchRunning = false;
                RefreshSnapshots();
            }
        }

        [RelayCommand]
        private void CancelDeviceBatch()
        {
            if (_batchCts is not null)
            {
                _batchCts.Cancel();
                _batchCts.Dispose();
                _batchCts = null;
            }
        }

        [RelayCommand]
        private void ClearTab2Logs() => Tab2Logs.Clear();

        public void RefreshSnapshots()
        {
            if (_disposed)
            {
                return;
            }

            DrainLogs(_pendingTab1Logs, Tab1Logs);
            DrainLogs(_pendingTab2Logs, Tab2Logs);

            PipelineSnapshot = _pipeline.GetSnapshot();
            IsPipelineRunning = PipelineSnapshot.IsRunning;
            OnPropertyChanged(nameof(ProducedText));
            OnPropertyChanged(nameof(ConsumedText));
            OnPropertyChanged(nameof(ThroughputSubText));
            OnPropertyChanged(nameof(DroppedText));
            OnPropertyChanged(nameof(BackpressureSubText));
            OnPropertyChanged(nameof(QueueDepthText));
            OnPropertyChanged(nameof(LatencySubText));

            OrchestrationSnapshot = _orchestrator.GetSnapshot();
            OnPropertyChanged(nameof(ConcurrencyCardText));
            OnPropertyChanged(nameof(PeakConcurrencySubText));
            OnPropertyChanged(nameof(ElapsedCardText));
            OnPropertyChanged(nameof(SerialSubText));
            OnPropertyChanged(nameof(SpeedupCardText));
            OnPropertyChanged(nameof(BatchOutcomeText));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _uiTimer?.Stop();
            CancelResponsiveness();
            CancelDeviceBatch();
            _pipeline.Dispose();
            IsPipelineRunning = false;
            IsBatchRunning = false;
        }

        partial void OnProduceRateHzChanged(double value) => _pipeline.UpdateLiveRates(value, ProcessTimeMs);
        partial void OnProcessTimeMsChanged(int value) => _pipeline.UpdateLiveRates(ProduceRateHz, value);

        partial void OnMaxConcurrencyChanged(int value)
        {
            if (!IsBatchRunning)
            {
                _orchestrator.InitializeIdle(BuildDeviceSpecs(), value);
                RefreshSnapshots();
            }
        }

        partial void OnInjectTimeoutOnTmp01Changed(bool value) => RefreshIdleBatchSpecs();
        partial void OnInjectFaultOnBar01Changed(bool value) => RefreshIdleBatchSpecs();

        private void RefreshIdleBatchSpecs()
        {
            if (!IsBatchRunning)
            {
                _orchestrator.InitializeIdle(BuildDeviceSpecs(), MaxConcurrency);
                RefreshSnapshots();
            }
        }

        private IReadOnlyList<DeviceSpec> BuildDeviceSpecs() =>
        [
            new(1, "CAM-01 · 顶视定位相机", "Vision", 380),
            new(2, "CAM-02 · 侧视缺陷相机", "Vision", 520),
            new(3, "PLC-01 · Modbus 主站", "PLC", 240),
            new(4, "PLC-02 · 伺服 IO 模块", "PLC", 310),
            new(5, "BAR-01 · 进料扫码枪", "Scanner", 460, InjectFault: InjectFaultOnBar01),
            new(6, "BAR-02 · 出料扫码枪", "Scanner", 420),
            new(7, "TMP-01 · 固化炉温控仪", "Sensor", 560, InjectTimeout: InjectTimeoutOnTmp01),
            new(8, "TMP-02 · 冷却区温控仪", "Sensor", 490),
        ];

        private static async Task ExecuteAsyncWorkAsync(IProgress<int> progress, CancellationToken ct)
        {
            const int steps = 20;
            for (var i = 1; i <= steps; i++)
            {
                await Task.Delay(60, ct).ConfigureAwait(false);
                progress.Report(i * 100 / steps);
            }
        }

        // Deliberately omits ConfigureAwait(false) to demonstrate WPF SynchronizationContext deadlock under .Wait().
        private static async Task SimulatedLibraryWithoutConfigureAwaitAsync()
        {
            await Task.Delay(100);
        }

        private static (bool Completed, int ContinuationThreadId) RunWithConfigureAwaitFalseHelper()
        {
            var task = SimulatedLibraryWithConfigureAwaitFalseAsync();
            return task.Wait(TimeSpan.FromMilliseconds(900))
                ? (true, task.Result)
                : (false, 0);
        }

        public static async Task<int> SimulatedLibraryWithConfigureAwaitFalseAsync()
        {
            await Task.Delay(100).ConfigureAwait(false);
            return Environment.CurrentManagedThreadId;
        }

        private void EnqueueTab1(string level, string message)
        {
            _pendingTab1Logs.Enqueue(new AsyncLogEntry(DateTime.Now, Environment.CurrentManagedThreadId, level, message));
            RefreshSnapshots();
        }

        private static void DrainLogs(ConcurrentQueue<AsyncLogEntry> queue, ObservableCollection<AsyncLogEntry> target)
        {
            while (queue.TryDequeue(out var entry))
            {
                target.Insert(0, entry);
                while (target.Count > MaxLogEntries)
                {
                    target.RemoveAt(target.Count - 1);
                }
            }
        }
    }
}
