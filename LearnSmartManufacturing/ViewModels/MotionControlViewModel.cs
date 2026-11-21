using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.Motion;
using LearnSmartManufacturing.Services.Motion;

namespace LearnSmartManufacturing.ViewModels
{
    public record ProfileOption(string Name, TrajectoryProfileType Value);

    public record HomingModeOption(string Name, HomingMode Value);

    public partial class MotionControlViewModel : ObservableObject, IDisposable
    {
        private const int MaxLogEntries = 120;

        private readonly IMotionAxis _axis;
        private readonly ISimulatedMotionAxis? _simulatedAxis;
        private readonly SimulatedXyMotionStage? _ownedFallbackStage;
        private readonly ConcurrentQueue<MotionLogEntry> _pendingLogs = new();
        private readonly DispatcherTimer? _uiTimer;

        private CancellationTokenSource? _moveCts;
        private AxisSnapshot _snapshot;
        private double _previewDistanceMm = 100.0;
        private bool _disposed;

        public MotionControlViewModel(IMotionAxis axis, IXyMotionStage? xyStage = null)
        {
            _axis = axis;
            _simulatedAxis = axis as ISimulatedMotionAxis;
            if (xyStage is null)
            {
                _ownedFallbackStage = new SimulatedXyMotionStage(startBackgroundLoop: false);
                xyStage = _ownedFallbackStage;
            }

            XyStage = new XyStageViewModel(xyStage);

            ProfileOptions =
            [
                new ProfileOption("S 型 7 段加减速", TrajectoryProfileType.SCurve),
                new ProfileOption("T 型梯形加减速", TrajectoryProfileType.Trapezoidal),
            ];
            SelectedProfile = ProfileOptions[0];

            HomingModes =
            [
                new HomingModeOption("负限位 → 原点精定位", HomingMode.NegativeLimitThenOrigin),
                new HomingModeOption("原点开关精定位", HomingMode.OriginSwitchOnly),
            ];
            SelectedHomingMode = HomingModes[0];

            _axis.LogAdded += OnAxisLogAdded;
            _snapshot = _axis.GetSnapshot();
            RebuildPreviewPlans();

            // Decouple 200 Hz background physics loop from UI updates via a 40 Hz snapshot timer.
            if (Application.Current is not null)
            {
                _uiTimer = new DispatcherTimer(DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(25),
                };
                _uiTimer.Tick += (_, _) => RefreshFromAxis();
                _uiTimer.Start();
            }
        }

        public string Heading => "运动控制";

        public XyStageViewModel XyStage { get; }

        public string AxisName => _axis.Name;
        public double HardNegativeLimitMm => _axis.HardNegativeLimitMm;
        public double HardPositiveLimitMm => _axis.HardPositiveLimitMm;
        public double SoftNegativeLimitMm => _axis.SoftNegativeLimitMm;
        public double SoftPositiveLimitMm => _axis.SoftPositiveLimitMm;
        public bool CanInjectFault => _simulatedAxis is not null;

        public IReadOnlyList<ProfileOption> ProfileOptions { get; }
        public IReadOnlyList<HomingModeOption> HomingModes { get; }
        public ObservableCollection<MotionLogEntry> Logs { get; } = [];

        [ObservableProperty]
        public partial ProfileOption SelectedProfile { get; set; }

        [ObservableProperty]
        public partial HomingModeOption SelectedHomingMode { get; set; }

        [ObservableProperty]
        public partial bool ShowComparison { get; set; } = true;

        [ObservableProperty]
        public partial string PulsePerMmText { get; set; } = "1000";

        [ObservableProperty]
        public partial string TargetPositionText { get; set; } = "120";

        [ObservableProperty]
        public partial string RelativeStepText { get; set; } = "50";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxVelocityText))]
        public partial double MaxVelocityMmPerSec { get; set; } = 100;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxAccelerationText))]
        public partial double MaxAccelerationMmPerSec2 { get; set; } = 400;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MaxJerkMmPerSec3Text))]
        public partial double MaxJerkMmPerSec3 { get; set; } = 2000;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(JogVelocityText))]
        public partial double JogVelocityMmPerSec { get; set; } = 40;

        [ObservableProperty]
        public partial TrajectoryPlan? PrimaryPlan { get; private set; }

        [ObservableProperty]
        public partial TrajectoryPlan? ComparisonPlan { get; private set; }

        [ObservableProperty]
        public partial double PlaybackTimeSec { get; private set; } = -1;

        public string MaxVelocityText => $"{MaxVelocityMmPerSec:F0} mm/s";
        public string MaxAccelerationText => $"{MaxAccelerationMmPerSec2:F0} mm/s²";
        public string MaxJerkMmPerSec3Text => $"{MaxJerkMmPerSec3:F0} mm/s³";
        public string JogVelocityText => $"{JogVelocityMmPerSec:F0} mm/s";

        // Live telemetry from AxisSnapshot
        public AxisState State => _snapshot.State;
        public string StateDisplay => _snapshot.State switch
        {
            AxisState.Disabled => "Disabled · 未使能",
            AxisState.Standstill => "Standstill · 使能待机",
            AxisState.Homing => "Homing · 回零中",
            AxisState.DiscreteMotion => "DiscreteMotion · 定位中",
            AxisState.ContinuousMotion => "ContinuousMotion · JOG 点动",
            AxisState.Stopping => "Stopping · 减速停止",
            AxisState.ErrorStop => "ErrorStop · 故障急停",
            _ => _snapshot.State.ToString(),
        };

        public bool IsServoOn => _snapshot.IsServoOn;
        public bool IsHomed => _snapshot.IsHomed;
        public bool IsInPosition => _snapshot.IsInPosition;
        public bool IsMoving => _snapshot.IsMoving;
        public bool PositiveLimit => _snapshot.PositiveLimit;
        public bool NegativeLimit => _snapshot.NegativeLimit;
        public bool OriginSensor => _snapshot.OriginSensor;
        public bool HasAlarm => _snapshot.AlarmCode != MotionAlarmCode.None;
        public string AlarmBannerText => HasAlarm ? $"⚠ {_snapshot.AlarmMessage}" : string.Empty;

        public double CommandPositionMm => _snapshot.CommandPositionMm;
        public double ActualPositionMm => _snapshot.ActualPositionMm;
        public double TargetPositionMm => _snapshot.TargetPositionMm;

        public string CommandPosText => $"{_snapshot.CommandPositionMm:F3} mm";
        public string CommandPulseText => $"{_snapshot.CommandPulse:N0} pulse";
        public string ActualPosText => $"{_snapshot.ActualPositionMm:F3} mm";
        public string PulseEquivText => $"电子齿轮 {_snapshot.PulsePerMm:F0} pulse/mm";
        public string FollowingErrorText => $"{_snapshot.FollowingErrorMm * 1000.0:+0.0;-0.0;0.0} μm";
        public string FollowingErrorMmText => $"{_snapshot.FollowingErrorMm:+0.000;-0.000;0.000} mm";
        public string VelocityText => $"{_snapshot.VelocityMmPerSec:+0.0;-0.0;0.0} mm/s";
        public string PlanSummaryText => PrimaryPlan is { TotalDurationSec: > 0 } p
            ? $"规划 T={p.TotalDurationSec:F3}s · {(p.ReachedCruiseVelocity ? "达巡航速" : "短行程退化")}"
            : "等待规划";

        [RelayCommand]
        private void ServoOn()
        {
            _axis.ServoOn();
            RefreshFromAxis();
        }

        [RelayCommand]
        private void ServoOff()
        {
            CancelPendingMove();
            _axis.ServoOff();
            RefreshFromAxis();
        }

        [RelayCommand]
        private void ResetAlarm()
        {
            _axis.ResetAlarm();
            RefreshFromAxis();
        }

        [RelayCommand]
        private void EmergencyStop()
        {
            CancelPendingMove();
            _axis.EmergencyStop();
            RefreshFromAxis();
        }

        [RelayCommand]
        private void Stop()
        {
            CancelPendingMove();
            _axis.Stop();
            RefreshFromAxis();
        }

        [RelayCommand]
        private async Task HomeAsync()
        {
            var ct = ResetMoveCts();
            try
            {
                await _axis.HomeAsync(SelectedHomingMode.Value, searchVelocityMmPerSec: 60, creepVelocityMmPerSec: 8, accelerationMmPerSec2: 400, ct);
            }
            catch (OperationCanceledException)
            {
                // Cancelled by Stop / E-Stop / page unload.
            }
            finally
            {
                RefreshFromAxis();
            }
        }

        [RelayCommand]
        private async Task MoveAbsoluteAsync()
        {
            if (!TryParseFinite(TargetPositionText, out var target))
            {
                EnqueueUiLog("Warn", "请输入有效的绝对目标位置 (mm)。");
                return;
            }

            _previewDistanceMm = target - _snapshot.CommandPositionMm;
            RebuildPreviewPlans();

            var ct = ResetMoveCts();
            try
            {
                await _axis.MoveAbsoluteAsync(target, MaxVelocityMmPerSec, MaxAccelerationMmPerSec2, MaxJerkMmPerSec3, SelectedProfile.Value, ct);
            }
            catch (OperationCanceledException)
            {
                // Cancelled by Stop / E-Stop / page unload.
            }
            finally
            {
                RefreshFromAxis();
            }
        }

        [RelayCommand]
        private Task MoveRelativePositiveAsync() => MoveRelativeCoreAsync(1);

        [RelayCommand]
        private Task MoveRelativeNegativeAsync() => MoveRelativeCoreAsync(-1);

        public void StartJog(int direction)
        {
            _axis.StartJog(direction, JogVelocityMmPerSec, MaxAccelerationMmPerSec2);
            RefreshFromAxis();
        }

        public void StopJog()
        {
            _axis.StopJog();
            RefreshFromAxis();
        }

        [RelayCommand]
        private void InjectPositiveLimit()
        {
            _simulatedAxis?.InjectFault(MotionAlarmCode.PositiveLimit);
            RefreshFromAxis();
        }

        [RelayCommand]
        private void InjectNegativeLimit()
        {
            _simulatedAxis?.InjectFault(MotionAlarmCode.NegativeLimit);
            RefreshFromAxis();
        }

        [RelayCommand]
        private void InjectFollowingError()
        {
            _simulatedAxis?.InjectFault(MotionAlarmCode.FollowingErrorExceeded);
            RefreshFromAxis();
        }

        [RelayCommand]
        private void InjectServoFault()
        {
            _simulatedAxis?.InjectFault(MotionAlarmCode.ServoFault);
            RefreshFromAxis();
        }

        [RelayCommand]
        private void ClearLogs() => Logs.Clear();

        /// <summary>Pulls the latest thread-safe telemetry snapshot and drains queued event logs.</summary>
        public void RefreshFromAxis()
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

            _snapshot = _axis.GetSnapshot();

            if (_snapshot.State == AxisState.DiscreteMotion && _snapshot.ActivePlan is { } activePlan)
            {
                if (!ReferenceEquals(PrimaryPlan, activePlan))
                {
                    PrimaryPlan = activePlan;
                    var altType = activePlan.ProfileType == TrajectoryProfileType.SCurve
                        ? TrajectoryProfileType.Trapezoidal
                        : TrajectoryProfileType.SCurve;
                    ComparisonPlan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(
                        activePlan.TotalDistanceMm, MaxVelocityMmPerSec, MaxAccelerationMmPerSec2, MaxJerkMmPerSec3, altType));
                }

                PlaybackTimeSec = _snapshot.ElapsedMoveTimeSec;
            }
            else if (PlaybackTimeSec >= 0)
            {
                PlaybackTimeSec = -1;
            }

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
            _axis.LogAdded -= OnAxisLogAdded;
            CancelPendingMove();
            _axis.Stop();
            XyStage.Dispose();
            _ownedFallbackStage?.Dispose();
        }

        partial void OnSelectedProfileChanged(ProfileOption value) => RebuildPreviewPlans();
        partial void OnMaxVelocityMmPerSecChanged(double value) => RebuildPreviewPlans();
        partial void OnMaxAccelerationMmPerSec2Changed(double value) => RebuildPreviewPlans();
        partial void OnMaxJerkMmPerSec3Changed(double value) => RebuildPreviewPlans();

        partial void OnPulsePerMmTextChanged(string value)
        {
            if (TryParsePositive(value, out var pulsePerMm))
            {
                _axis.SetPulsePerMm(pulsePerMm);
                RefreshFromAxis();
            }
        }

        partial void OnTargetPositionTextChanged(string value)
        {
            if (TryParseFinite(value, out var target))
            {
                var delta = target - _snapshot.CommandPositionMm;
                if (Math.Abs(delta) > 1e-6)
                {
                    _previewDistanceMm = delta;
                    RebuildPreviewPlans();
                }
            }
        }

        partial void OnRelativeStepTextChanged(string value)
        {
            if (TryParsePositive(value, out var step))
            {
                _previewDistanceMm = step;
                RebuildPreviewPlans();
            }
        }

        private async Task MoveRelativeCoreAsync(int sign)
        {
            if (!TryParsePositive(RelativeStepText, out var step))
            {
                EnqueueUiLog("Warn", "请输入大于 0 的相对步距 (mm)。");
                return;
            }

            var delta = sign * step;
            _previewDistanceMm = delta;
            RebuildPreviewPlans();

            var ct = ResetMoveCts();
            try
            {
                await _axis.MoveRelativeAsync(delta, MaxVelocityMmPerSec, MaxAccelerationMmPerSec2, MaxJerkMmPerSec3, SelectedProfile.Value, ct);
            }
            catch (OperationCanceledException)
            {
                // Cancelled by Stop / E-Stop / page unload.
            }
            finally
            {
                RefreshFromAxis();
            }
        }

        private void RebuildPreviewPlans()
        {
            if (_snapshot.State == AxisState.DiscreteMotion || SelectedProfile is null)
            {
                return;
            }

            var primaryType = SelectedProfile.Value;
            var altType = primaryType == TrajectoryProfileType.SCurve
                ? TrajectoryProfileType.Trapezoidal
                : TrajectoryProfileType.SCurve;

            PrimaryPlan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(
                _previewDistanceMm, MaxVelocityMmPerSec, MaxAccelerationMmPerSec2, MaxJerkMmPerSec3, primaryType));
            ComparisonPlan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(
                _previewDistanceMm, MaxVelocityMmPerSec, MaxAccelerationMmPerSec2, MaxJerkMmPerSec3, altType));
            OnPropertyChanged(nameof(PlanSummaryText));
        }

        private CancellationToken ResetMoveCts()
        {
            CancelPendingMove();
            _moveCts = new CancellationTokenSource();
            return _moveCts.Token;
        }

        private void CancelPendingMove()
        {
            if (_moveCts is not null)
            {
                _moveCts.Cancel();
                _moveCts.Dispose();
                _moveCts = null;
            }
        }

        private void OnAxisLogAdded(MotionLogEntry entry) => _pendingLogs.Enqueue(entry);

        private void EnqueueUiLog(string level, string message)
        {
            _pendingLogs.Enqueue(new MotionLogEntry(DateTime.Now, level, message));
            RefreshFromAxis();
        }

        private void RaiseTelemetryChanged()
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(StateDisplay));
            OnPropertyChanged(nameof(IsServoOn));
            OnPropertyChanged(nameof(IsHomed));
            OnPropertyChanged(nameof(IsInPosition));
            OnPropertyChanged(nameof(IsMoving));
            OnPropertyChanged(nameof(PositiveLimit));
            OnPropertyChanged(nameof(NegativeLimit));
            OnPropertyChanged(nameof(OriginSensor));
            OnPropertyChanged(nameof(HasAlarm));
            OnPropertyChanged(nameof(AlarmBannerText));
            OnPropertyChanged(nameof(CommandPositionMm));
            OnPropertyChanged(nameof(ActualPositionMm));
            OnPropertyChanged(nameof(TargetPositionMm));
            OnPropertyChanged(nameof(CommandPosText));
            OnPropertyChanged(nameof(CommandPulseText));
            OnPropertyChanged(nameof(ActualPosText));
            OnPropertyChanged(nameof(PulseEquivText));
            OnPropertyChanged(nameof(FollowingErrorText));
            OnPropertyChanged(nameof(FollowingErrorMmText));
            OnPropertyChanged(nameof(VelocityText));
            OnPropertyChanged(nameof(PlanSummaryText));
        }

        private static bool TryParseFinite(string? text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);

        private static bool TryParsePositive(string? text, out double value) =>
            TryParseFinite(text, out value) && value > 0;
    }
}
