using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.Motion;
using LearnSmartManufacturing.Services.Motion;

namespace LearnSmartManufacturing.ViewModels
{
    public record XyModeOption(string Name, XyMotionMode Value);

    public record XyRecipePreset(string Name, string Description);

    public partial class XyStageViewModel : ObservableObject, IDisposable
    {
        private const int MaxLogEntries = 120;

        private readonly IXyMotionStage _stage;
        private readonly ConcurrentQueue<MotionLogEntry> _pendingLogs = new();
        private readonly DispatcherTimer? _uiTimer;

        private CancellationTokenSource? _runCts;
        private int _stepCursor;
        private bool _disposed;

        public XyStageViewModel(IXyMotionStage stage)
        {
            _stage = stage;
            StageSnapshot = _stage.GetSnapshot();

            ModeOptions =
            [
                new XyModeOption("直线插补 (G01)", XyMotionMode.LinearInterpolation),
                new XyModeOption("独立点到点 (PTP)", XyMotionMode.IndependentPtp),
                new XyModeOption("顺时针圆弧 (G02)", XyMotionMode.CircularArcCw),
                new XyModeOption("逆时针圆弧 (G03)", XyMotionMode.CircularArcCcw),
            ];
            SelectedMode = ModeOptions[0];

            ProfileOptions =
            [
                new ProfileOption("S 型加减速", TrajectoryProfileType.SCurve),
                new ProfileOption("T 型加减速", TrajectoryProfileType.Trapezoidal),
            ];
            SelectedProfile = ProfileOptions[0];

            RecipePresets =
            [
                new XyRecipePreset("3×3 视觉阵列停拍 (AOI)", "9 站点逐点定位，到位稳态后定点触发拍照"),
                new XyRecipePreset("蛇形 PSO 飞拍扫描", "3 行高速连续扫描，不停车按等距触发 PSO 飞拍"),
                new XyRecipePreset("G01/G02/G03 综合轮廓", "演示直线插补、顺圆/逆圆插补与 PTP 跨区定位"),
                new XyRecipePreset("PTP 折线 vs 直线对比", "对角线往返，切换 PTP/G01 直观对比路径形态"),
            ];

            _stage.LogAdded += OnStageLogAdded;

            SelectedRecipePreset = RecipePresets[0];
            ApplyPreset(RecipePresets[0]);
            RebuildPreviewPlan();

            if (Application.Current is not null)
            {
                _uiTimer = new DispatcherTimer(DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(25),
                };
                _uiTimer.Tick += (_, _) => RefreshFromStage();
                _uiTimer.Start();
            }
        }

        public double WorkspaceWidthMm => _stage.XMaxMm;
        public double WorkspaceHeightMm => _stage.YMaxMm;

        public IReadOnlyList<XyModeOption> ModeOptions { get; }
        public IReadOnlyList<ProfileOption> ProfileOptions { get; }
        public IReadOnlyList<XyRecipePreset> RecipePresets { get; }
        public ObservableCollection<TeachPoint> TeachPoints { get; } = [];
        public ObservableCollection<MotionLogEntry> Logs { get; } = [];

        [ObservableProperty]
        public partial XyStageSnapshot StageSnapshot { get; private set; }

        [ObservableProperty]
        public partial IReadOnlyList<TeachPoint> TeachPointsSnapshot { get; private set; } = [];

        [ObservableProperty]
        public partial TeachPoint? SelectedTeachPoint { get; set; }

        [ObservableProperty]
        public partial XyModeOption SelectedMode { get; set; }

        [ObservableProperty]
        public partial ProfileOption SelectedProfile { get; set; }

        [ObservableProperty]
        public partial XyRecipePreset? SelectedRecipePreset { get; set; }

        [ObservableProperty]
        public partial string TargetXText { get; set; } = "160";

        [ObservableProperty]
        public partial string TargetYText { get; set; } = "110";

        [ObservableProperty]
        public partial string ArcRadiusText { get; set; } = "0";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FeedVelocityText))]
        public partial double FeedVelocityMmPerSec { get; set; } = 140;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxAccelText))]
        public partial double MaxAccelerationMmPerSec2 { get; set; } = 600;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxJerkText))]
        public partial double MaxJerkMmPerSec3 { get; set; } = 3000;

        [ObservableProperty]
        public partial bool PsoEnabled { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PsoIntervalText))]
        public partial double PsoIntervalMm { get; set; } = 15;

        [ObservableProperty]
        public partial bool LoopRecipe { get; set; }

        [ObservableProperty]
        public partial bool IsRecipeRunning { get; private set; }

        [ObservableProperty]
        public partial string LastCycleTimeText { get; private set; } = "尚未执行配方";

        [ObservableProperty]
        public partial XyTrajectoryPlan? PreviewPlan { get; private set; }

        public bool IsArcMode => SelectedMode?.Value is XyMotionMode.CircularArcCw or XyMotionMode.CircularArcCcw;
        public string FeedVelocityText => $"{FeedVelocityMmPerSec:F0} mm/s";
        public string MaxAccelText => $"{MaxAccelerationMmPerSec2:F0} mm/s²";
        public string MaxJerkText => $"{MaxJerkMmPerSec3:F0} mm/s³";
        public string PsoIntervalText => $"{PsoIntervalMm:F0} mm";

        public AxisState State => StageSnapshot.State;
        public bool HasAlarm => StageSnapshot.AlarmCode != MotionAlarmCode.None;
        public string AlarmBannerText => HasAlarm ? $"⚠ {StageSnapshot.AlarmMessage}" : string.Empty;
        public string StateDisplay => StageSnapshot.State switch
        {
            AxisState.Disabled => "Disabled · 未使能",
            AxisState.Standstill => "Standstill · XY 待机",
            AxisState.Homing => "Homing · XY 回零中",
            AxisState.DiscreteMotion => "Moving · 插补运行中",
            AxisState.ErrorStop => "ErrorStop · XY 急停报警",
            _ => StageSnapshot.State.ToString(),
        };

        public string PosXText => $"{StageSnapshot.ActualX:F2} mm";
        public string PosYText => $"{StageSnapshot.ActualY:F2} mm";
        public string CommandCoordText => $"规划 ({StageSnapshot.CommandX:F2}, {StageSnapshot.CommandY:F2})";
        public string FollowingErrorText => $"跟随误差 {StageSnapshot.FollowingErrorMm * 1000.0:F1} μm";
        public string FeedSpeedText => $"{StageSnapshot.FeedSpeedMmPerSec:F1} mm/s";
        public string VelocityComponentText => $"Vx={StageSnapshot.Vx:+0.0;-0.0;0.0} · Vy={StageSnapshot.Vy:+0.0;-0.0;0.0}";
        public string TriggerSummaryText => $"停拍 {StageSnapshot.StopCaptureCount} / 飞拍 {StageSnapshot.PsoTriggerCount}";

        public void SetTargetFromCanvas(Point2D point)
        {
            TargetXText = point.X.ToString("0.#", CultureInfo.CurrentCulture);
            TargetYText = point.Y.ToString("0.#", CultureInfo.CurrentCulture);
            RebuildPreviewPlan();
        }

        [RelayCommand]
        private void ServoOn()
        {
            _stage.ServoOn();
            RefreshFromStage();
        }

        [RelayCommand]
        private void ServoOff()
        {
            CancelRun();
            _stage.ServoOff();
            RefreshFromStage();
        }

        [RelayCommand]
        private void ResetAlarm()
        {
            _stage.ResetAlarm();
            RefreshFromStage();
        }

        [RelayCommand]
        private void EmergencyStop()
        {
            CancelRun();
            _stage.EmergencyStop();
            RefreshFromStage();
        }

        [RelayCommand]
        private void Stop()
        {
            CancelRun();
            _stage.Stop();
            RefreshFromStage();
        }

        [RelayCommand]
        private async Task HomeAsync()
        {
            var ct = ResetRunCts();
            try
            {
                await _stage.HomeAsync(100, ct);
            }
            catch (OperationCanceledException)
            {
                // Stopped by user or page unload.
            }
            finally
            {
                RefreshFromStage();
                RebuildPreviewPlan();
            }
        }

        [RelayCommand]
        private async Task MoveToTargetAsync()
        {
            if (!TryParseTarget(out var target, out var arcRadius))
            {
                EnqueueUiLog("Warn", "请输入有效的目标 X / Y 坐标及圆弧半径 (≥ 0)。");
                return;
            }

            var ct = ResetRunCts();
            try
            {
                await _stage.MoveToAsync(target, SelectedMode.Value, FeedVelocityMmPerSec,
                    MaxAccelerationMmPerSec2, MaxJerkMmPerSec3, SelectedProfile.Value,
                    arcRadius, PsoEnabled, PsoIntervalMm, ct);
            }
            catch (OperationCanceledException)
            {
                // Stopped by user.
            }
            finally
            {
                RefreshFromStage();
                RebuildPreviewPlan();
            }
        }

        [RelayCommand]
        private void ClearTrail()
        {
            _stage.ClearHistory();
            RefreshFromStage();
        }

        [RelayCommand]
        private void TeachCurrentPoint()
        {
            var snap = _stage.GetSnapshot();
            var idx = TeachPoints.Count + 1;
            var pt = new TeachPoint(idx, $"示教点 P{idx}", Math.Round(snap.ActualX, 2), Math.Round(snap.ActualY, 2),
                SelectedMode.Value, ParseNonNegativeOrZero(ArcRadiusText), CaptureTrigger: true, DwellMs: 100);
            TeachPoints.Add(pt);
            SelectedTeachPoint = pt;
            SyncTeachPointsSnapshot();
            EnqueueUiLog("Info", $"已示教当前点位 P{idx} ({pt.X:F1}, {pt.Y:F1}) [{pt.ModeShort}]。");
        }

        [RelayCommand]
        private void AddTargetAsPoint()
        {
            if (!TryParseTarget(out var target, out var arcRadius))
            {
                EnqueueUiLog("Warn", "目标坐标无效，无法添加到示教表。");
                return;
            }

            var idx = TeachPoints.Count + 1;
            var pt = new TeachPoint(idx, $"点位 P{idx}", target.X, target.Y, SelectedMode.Value, arcRadius, CaptureTrigger: true, DwellMs: 100);
            TeachPoints.Add(pt);
            SelectedTeachPoint = pt;
            SyncTeachPointsSnapshot();
            EnqueueUiLog("Info", $"已添加目标点位 P{idx} ({pt.X:F1}, {pt.Y:F1}) [{pt.ModeShort}]。");
        }

        [RelayCommand]
        private void RemoveSelectedPoint()
        {
            if (SelectedTeachPoint is null)
            {
                return;
            }

            var removeIdx = TeachPoints.IndexOf(SelectedTeachPoint);
            if (removeIdx < 0)
            {
                return;
            }

            TeachPoints.RemoveAt(removeIdx);
            ReindexTeachPoints();
            SelectedTeachPoint = TeachPoints.Count > 0 ? TeachPoints[Math.Min(removeIdx, TeachPoints.Count - 1)] : null;
            SyncTeachPointsSnapshot();
        }

        [RelayCommand]
        private void ClearPoints()
        {
            TeachPoints.Clear();
            SelectedTeachPoint = null;
            _stepCursor = 0;
            SyncTeachPointsSnapshot();
        }

        [RelayCommand]
        private async Task StepNextPointAsync()
        {
            if (TeachPoints.Count == 0 || IsRecipeRunning)
            {
                return;
            }

            if (_stepCursor >= TeachPoints.Count)
            {
                _stepCursor = 0;
            }

            var pt = TeachPoints[_stepCursor];
            SelectedTeachPoint = pt;
            var ct = ResetRunCts();

            try
            {
                var ok = await ExecuteTeachPointAsync(pt, ct);
                if (ok)
                {
                    _stepCursor = (_stepCursor + 1) % TeachPoints.Count;
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled.
            }
            finally
            {
                RefreshFromStage();
                RebuildPreviewPlan();
            }
        }

        [RelayCommand]
        private async Task RunRecipeAsync()
        {
            if (TeachPoints.Count == 0 || IsRecipeRunning)
            {
                return;
            }

            var ct = ResetRunCts();
            IsRecipeRunning = true;
            _stage.ClearHistory();
            var sw = Stopwatch.StartNew();
            var cycleCount = 0;

            try
            {
                EnqueueUiLog("Info", $"启动配方运行：共 {TeachPoints.Count} 个点位，PSO 飞拍={(PsoEnabled ? $"开启 ({PsoIntervalMm:F0}mm)" : "关闭")}。");
                do
                {
                    cycleCount++;
                    var cycleSw = Stopwatch.StartNew();
                    var points = TeachPoints.ToArray();
                    foreach (var pt in points)
                    {
                        ct.ThrowIfCancellationRequested();
                        SelectedTeachPoint = pt;
                        var ok = await ExecuteTeachPointAsync(pt, ct);
                        if (!ok)
                        {
                            return;
                        }
                    }

                    cycleSw.Stop();
                    LastCycleTimeText = $"单周期耗时 {cycleSw.Elapsed.TotalSeconds:F2} s (第 {cycleCount} 轮)";
                    EnqueueUiLog("Info", $"配方第 {cycleCount} 轮完成，周期耗时 {cycleSw.Elapsed.TotalSeconds:F2} s。");
                }
                while (LoopRecipe && !ct.IsCancellationRequested);
            }
            catch (OperationCanceledException)
            {
                EnqueueUiLog("Warn", $"配方运行已中止，累计运行 {sw.Elapsed.TotalSeconds:F2} s。");
            }
            finally
            {
                IsRecipeRunning = false;
                RefreshFromStage();
                RebuildPreviewPlan();
            }
        }

        [RelayCommand]
        private void LoadSelectedPreset()
        {
            if (SelectedRecipePreset is not null)
            {
                ApplyPreset(SelectedRecipePreset);
            }
        }

        [RelayCommand]
        private void ClearLogs() => Logs.Clear();

        public void RefreshFromStage()
        {
            if (_disposed)
            {
                return;
            }

            while (_pendingLogs.TryDequeue(out var entry))
            {
                Logs.Insert(0, entry);
                while (Logs.Count > MaxLogEntries)
                {
                    Logs.RemoveAt(Logs.Count - 1);
                }
            }

            StageSnapshot = _stage.GetSnapshot();
            RaiseTelemetryChanged();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _uiTimer?.Stop();
            _stage.LogAdded -= OnStageLogAdded;
            CancelRun();
            _stage.Stop();
        }

        partial void OnSelectedModeChanged(XyModeOption value)
        {
            OnPropertyChanged(nameof(IsArcMode));
            RebuildPreviewPlan();
        }

        partial void OnSelectedProfileChanged(ProfileOption value) => RebuildPreviewPlan();
        partial void OnTargetXTextChanged(string value) => RebuildPreviewPlan();
        partial void OnTargetYTextChanged(string value) => RebuildPreviewPlan();
        partial void OnArcRadiusTextChanged(string value) => RebuildPreviewPlan();
        partial void OnFeedVelocityMmPerSecChanged(double value) => RebuildPreviewPlan();
        partial void OnMaxAccelerationMmPerSec2Changed(double value) => RebuildPreviewPlan();
        partial void OnMaxJerkMmPerSec3Changed(double value) => RebuildPreviewPlan();

        partial void OnSelectedTeachPointChanged(TeachPoint? value)
        {
            if (value is not null && !IsRecipeRunning)
            {
                TargetXText = value.X.ToString("0.#", CultureInfo.CurrentCulture);
                TargetYText = value.Y.ToString("0.#", CultureInfo.CurrentCulture);
                ArcRadiusText = value.ArcRadiusMm.ToString("0.#", CultureInfo.CurrentCulture);
                var match = ModeOptions.FirstOrDefault(m => m.Value == value.Mode);
                if (match is not null)
                {
                    SelectedMode = match;
                }
            }
        }

        private async Task<bool> ExecuteTeachPointAsync(TeachPoint pt, CancellationToken ct)
        {
            var usePsoOnSegment = PsoEnabled && pt.CaptureTrigger && pt.Mode != XyMotionMode.IndependentPtp;
            var ok = await _stage.MoveToAsync(
                new Point2D(pt.X, pt.Y),
                pt.Mode,
                FeedVelocityMmPerSec,
                MaxAccelerationMmPerSec2,
                MaxJerkMmPerSec3,
                SelectedProfile.Value,
                pt.ArcRadiusMm,
                usePsoOnSegment,
                PsoIntervalMm,
                ct);

            if (!ok)
            {
                return false;
            }

            // Stop-and-Capture AOI dwell when PSO fly-capture is off.
            if (pt.CaptureTrigger && !PsoEnabled)
            {
                if (pt.DwellMs > 0)
                {
                    await Task.Delay(pt.DwellMs, ct);
                }

                _stage.RecordCaptureMarker(new Point2D(pt.X, pt.Y), isPso: false);
                EnqueueUiLog("Info", $"定点拍照完成：{pt.Name} ({pt.X:F1}, {pt.Y:F1}) [整定 {pt.DwellMs} ms]。");
            }

            return true;
        }

        private void ApplyPreset(XyRecipePreset preset)
        {
            CancelRun();
            _stage.ClearHistory();
            TeachPoints.Clear();
            _stepCursor = 0;

            if (preset == RecipePresets[0])
            {
                // 3x3 snake AOI stop-and-capture grid
                PsoEnabled = false;
                SelectedMode = ModeOptions[0]; // G01
                var xs = new[] { 40.0, 100.0, 160.0 };
                var ys = new[] { 30.0, 75.0, 120.0 };
                var idx = 1;
                for (var row = 0; row < ys.Length; row++)
                {
                    var cols = row % 2 == 0 ? xs : xs.Reverse().ToArray();
                    foreach (var x in cols)
                    {
                        TeachPoints.Add(new TeachPoint(idx, $"工位 {row + 1}-{idx}", x, ys[row], XyMotionMode.LinearInterpolation, 0, CaptureTrigger: true, DwellMs: 120));
                        idx++;
                    }
                }
            }
            else if (preset == RecipePresets[1])
            {
                // Snake PSO fly-capture scan: 3 horizontal scan passes without stopping
                PsoEnabled = true;
                PsoIntervalMm = 15;
                SelectedMode = ModeOptions[0];
                TeachPoints.Add(new TeachPoint(1, "第1行起点", 20, 30, XyMotionMode.LinearInterpolation, 0, CaptureTrigger: false, DwellMs: 0));
                TeachPoints.Add(new TeachPoint(2, "第1行飞拍", 180, 30, XyMotionMode.LinearInterpolation, 0, CaptureTrigger: true, DwellMs: 0));
                TeachPoints.Add(new TeachPoint(3, "换行至第2行", 180, 75, XyMotionMode.LinearInterpolation, 0, CaptureTrigger: false, DwellMs: 0));
                TeachPoints.Add(new TeachPoint(4, "第2行飞拍", 20, 75, XyMotionMode.LinearInterpolation, 0, CaptureTrigger: true, DwellMs: 0));
                TeachPoints.Add(new TeachPoint(5, "换行至第3行", 20, 120, XyMotionMode.LinearInterpolation, 0, CaptureTrigger: false, DwellMs: 0));
                TeachPoints.Add(new TeachPoint(6, "第3行飞拍", 180, 120, XyMotionMode.LinearInterpolation, 0, CaptureTrigger: true, DwellMs: 0));
            }
            else if (preset == RecipePresets[2])
            {
                // Combined G01 / G02 / G03 / PTP industrial contour
                PsoEnabled = false;
                SelectedMode = ModeOptions[0];
                TeachPoints.Add(new TeachPoint(1, "轮廓起点", 40, 30, XyMotionMode.IndependentPtp, 0, CaptureTrigger: false, DwellMs: 0));
                TeachPoints.Add(new TeachPoint(2, "底边直线", 150, 30, XyMotionMode.LinearInterpolation, 0, CaptureTrigger: true, DwellMs: 80));
                TeachPoints.Add(new TeachPoint(3, "右侧逆圆", 150, 120, XyMotionMode.CircularArcCcw, 45, CaptureTrigger: true, DwellMs: 80));
                TeachPoints.Add(new TeachPoint(4, "顶边直线", 50, 120, XyMotionMode.LinearInterpolation, 0, CaptureTrigger: true, DwellMs: 80));
                TeachPoints.Add(new TeachPoint(5, "左侧顺圆", 40, 30, XyMotionMode.CircularArcCw, 65, CaptureTrigger: true, DwellMs: 80));
            }
            else
            {
                // Diagonal PTP dog-leg vs Linear straight-line comparison
                PsoEnabled = false;
                SelectedMode = ModeOptions[1]; // PTP
                TeachPoints.Add(new TeachPoint(1, "左下角", 20, 20, XyMotionMode.LinearInterpolation, 0, CaptureTrigger: false, DwellMs: 0));
                TeachPoints.Add(new TeachPoint(2, "右上角 (PTP折线)", 180, 120, XyMotionMode.IndependentPtp, 0, CaptureTrigger: true, DwellMs: 80));
                TeachPoints.Add(new TeachPoint(3, "返回左下 (G01直线)", 20, 20, XyMotionMode.LinearInterpolation, 0, CaptureTrigger: true, DwellMs: 80));
            }

            SelectedTeachPoint = TeachPoints.FirstOrDefault();
            SyncTeachPointsSnapshot();
            RefreshFromStage();
            RebuildPreviewPlan();
            EnqueueUiLog("Info", $"已载入预设配方“{preset.Name}”：{preset.Description}。");
        }

        private void RebuildPreviewPlan()
        {
            if (StageSnapshot is null || SelectedMode is null || SelectedProfile is null || !TryParseTarget(out var target, out var arcRadius))
            {
                PreviewPlan = null;
                return;
            }

            var start = new Point2D(StageSnapshot.CommandX, StageSnapshot.CommandY);
            PreviewPlan = XyTrajectoryPlanner.Plan(start, target, SelectedMode.Value,
                FeedVelocityMmPerSec, MaxAccelerationMmPerSec2, MaxJerkMmPerSec3, SelectedProfile.Value, arcRadius);
        }

        private void ReindexTeachPoints()
        {
            for (var i = 0; i < TeachPoints.Count; i++)
            {
                TeachPoints[i] = TeachPoints[i] with { Index = i + 1 };
            }
        }

        private void SyncTeachPointsSnapshot() => TeachPointsSnapshot = TeachPoints.ToArray();

        private CancellationToken ResetRunCts()
        {
            CancelRun();
            _runCts = new CancellationTokenSource();
            return _runCts.Token;
        }

        private void CancelRun()
        {
            if (_runCts is not null)
            {
                _runCts.Cancel();
                _runCts.Dispose();
                _runCts = null;
            }
        }

        private void OnStageLogAdded(MotionLogEntry entry) => _pendingLogs.Enqueue(entry);

        private void EnqueueUiLog(string level, string message)
        {
            _pendingLogs.Enqueue(new MotionLogEntry(DateTime.Now, level, message));
            RefreshFromStage();
        }

        private void RaiseTelemetryChanged()
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(HasAlarm));
            OnPropertyChanged(nameof(AlarmBannerText));
            OnPropertyChanged(nameof(StateDisplay));
            OnPropertyChanged(nameof(PosXText));
            OnPropertyChanged(nameof(PosYText));
            OnPropertyChanged(nameof(CommandCoordText));
            OnPropertyChanged(nameof(FollowingErrorText));
            OnPropertyChanged(nameof(FeedSpeedText));
            OnPropertyChanged(nameof(VelocityComponentText));
            OnPropertyChanged(nameof(TriggerSummaryText));
        }

        private bool TryParseTarget(out Point2D target, out double arcRadius)
        {
            target = default;
            arcRadius = 0;
            if (!TryParseFinite(TargetXText, out var x) || !TryParseFinite(TargetYText, out var y))
            {
                return false;
            }

            if (x < _stage.XMinMm || x > _stage.XMaxMm || y < _stage.YMinMm || y > _stage.YMaxMm)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(ArcRadiusText))
            {
                if (!TryParseFinite(ArcRadiusText, out arcRadius) || arcRadius < 0)
                {
                    return false;
                }
            }

            target = new Point2D(x, y);
            return true;
        }

        private static double ParseNonNegativeOrZero(string? text) =>
            TryParseFinite(text, out var v) && v >= 0 ? v : 0;

        private static bool TryParseFinite(string? text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);
    }
}
