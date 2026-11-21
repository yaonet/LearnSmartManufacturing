using LearnSmartManufacturing.Models.Motion;

namespace LearnSmartManufacturing.Services.Motion
{
    /// <summary>
    /// 200 Hz simulated 2D XY gantry stage supporting independent PTP, linear (G01),
    /// circular arc (G02/G03) interpolation, workspace soft-limit checks, and hardware-style
    /// position-synchronized output (PSO) fly-capture triggering.
    /// </summary>
    public sealed class SimulatedXyMotionStage : IXyMotionStage, IDisposable
    {
        private const double ServoLagTimeConstantSec = 0.0015;
        private const double TickIntervalSec = 0.005;
        private const double TrailSampleSpacingMm = 1.2;
        private const int MaxTrailPoints = 2500;

        private readonly Lock _sync = new();
        private readonly CancellationTokenSource _loopCts = new();
        private readonly Task? _loopTask;

        private AxisState _state = AxisState.Standstill;
        private bool _isServoOn = true;
        private bool _isHomed = true;
        private MotionAlarmCode _alarmCode = MotionAlarmCode.None;
        private string _alarmMessage = string.Empty;

        private double _cmdX = 20.0;
        private double _cmdY = 20.0;
        private double _actX = 20.0;
        private double _actY = 20.0;
        private double _vx;
        private double _vy;

        private XyTrajectoryPlan? _activePlan;
        private double _elapsedMoveTimeSec;
        private bool _isHomingMove;
        private bool _psoEnabled;
        private double _psoIntervalMm = 15.0;
        private double _nextPsoDistanceMm;

        private TaskCompletionSource<bool>? _activeMoveTcs;
        private CancellationTokenRegistration _activeMoveCancelReg;

        private readonly List<Point2D> _trail = [new(20.0, 20.0)];
        private readonly List<TriggerMarker> _triggers = [];
        private int _triggerSeq;
        private bool _disposed;

        public SimulatedXyMotionStage(bool startBackgroundLoop = true)
        {
            if (startBackgroundLoop)
            {
                _loopTask = Task.Run(RunPhysicsLoopAsync);
            }
        }

        public double XMinMm => 0.0;
        public double XMaxMm => 200.0;
        public double YMinMm => 0.0;
        public double YMaxMm => 150.0;

        public event Action<MotionLogEntry>? LogAdded;

        public XyStageSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return new XyStageSnapshot(
                    _state,
                    _isServoOn,
                    _isHomed,
                    _alarmCode,
                    _alarmMessage,
                    _cmdX,
                    _cmdY,
                    _actX,
                    _actY,
                    _vx,
                    _vy,
                    _elapsedMoveTimeSec,
                    _activePlan,
                    _trail.ToArray(),
                    _triggers.ToArray());
            }
        }

        public bool ServoOn()
        {
            MotionLogEntry? log;
            bool ok;
            lock (_sync)
            {
                if (_alarmCode != MotionAlarmCode.None || _state == AxisState.ErrorStop)
                {
                    ok = false;
                    log = MakeLog("Warn", "XY 平台存在未清除报警，请先清报警后再使能。");
                }
                else if (_isServoOn)
                {
                    return true;
                }
                else
                {
                    _isServoOn = true;
                    _state = AxisState.Standstill;
                    ok = true;
                    log = MakeLog("Info", "XY 双轴伺服已使能 (Standstill)。");
                }
            }

            Emit(log);
            return ok;
        }

        public void ServoOff()
        {
            TaskCompletionSource<bool>? tcs;
            CancellationTokenRegistration reg;
            MotionLogEntry? log;

            lock (_sync)
            {
                DetachActiveMoveLocked(out tcs, out reg);
                _isServoOn = false;
                _vx = _vy = 0;
                _activePlan = null;
                _isHomingMove = false;
                if (_state != AxisState.ErrorStop)
                {
                    _state = AxisState.Disabled;
                }

                log = MakeLog("Info", "XY 双轴伺服已断使能 (Disabled)。");
            }

            reg.Dispose();
            tcs?.TrySetResult(false);
            Emit(log);
        }

        public void ResetAlarm()
        {
            MotionLogEntry? log;
            lock (_sync)
            {
                _alarmCode = MotionAlarmCode.None;
                _alarmMessage = string.Empty;
                _actX = _cmdX;
                _actY = _cmdY;
                _vx = _vy = 0;
                _isServoOn = false;
                _state = AxisState.Disabled;
                log = MakeLog("Info", "XY 平台报警已复位，请重新使能。");
            }

            Emit(log);
        }

        public void EmergencyStop()
        {
            TaskCompletionSource<bool>? tcs;
            CancellationTokenRegistration reg;
            MotionLogEntry? log;

            lock (_sync)
            {
                DetachActiveMoveLocked(out tcs, out reg);
                _isServoOn = false;
                _isHomed = false;
                _vx = _vy = 0;
                _activePlan = null;
                _isHomingMove = false;
                _alarmCode = MotionAlarmCode.EmergencyStop;
                _alarmMessage = "XY 平台急停触发，已切断使能并失效零点";
                _state = AxisState.ErrorStop;
                log = MakeLog("Error", "XY 平台急停触发 (ErrorStop)：已切断使能，请复位并重新回零。");
            }

            reg.Dispose();
            tcs?.TrySetResult(false);
            Emit(log);
        }

        public void Stop()
        {
            TaskCompletionSource<bool>? tcs = null;
            CancellationTokenRegistration reg = default;
            MotionLogEntry? log = null;

            lock (_sync)
            {
                if (_state is AxisState.Disabled or AxisState.Standstill or AxisState.ErrorStop)
                {
                    return;
                }

                DetachActiveMoveLocked(out tcs, out reg);
                _vx = _vy = 0;
                _activePlan = null;
                _isHomingMove = false;
                _state = _isServoOn ? AxisState.Standstill : AxisState.Disabled;
                log = MakeLog("Info", $"XY 平台已停止，当前坐标 ({_cmdX:F2}, {_cmdY:F2}) mm。");
            }

            reg.Dispose();
            tcs?.TrySetResult(false);
            Emit(log);
        }

        public void ClearHistory()
        {
            lock (_sync)
            {
                _trail.Clear();
                _trail.Add(new Point2D(_actX, _actY));
                _triggers.Clear();
                _triggerSeq = 0;
            }
        }

        public void RecordCaptureMarker(Point2D position, bool isPso)
        {
            lock (_sync)
            {
                _triggerSeq++;
                _triggers.Add(new TriggerMarker(position, isPso, _triggerSeq));
            }
        }

        public Task<bool> HomeAsync(double speedMmPerSec, CancellationToken cancellationToken = default)
        {
            if (speedMmPerSec <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(speedMmPerSec));
            }

            MotionLogEntry? log;
            Task<bool> task;

            lock (_sync)
            {
                if (!_isServoOn || _state != AxisState.Standstill)
                {
                    log = MakeLog("Warn", "仅在使能待机 (Standstill) 状态下允许执行 XY 回零。");
                    task = Task.FromResult(false);
                }
                else
                {
                    var start = new Point2D(_cmdX, _cmdY);
                    var origin = new Point2D(0, 0);
                    var plan = XyTrajectoryPlanner.Plan(start, origin, XyMotionMode.LinearInterpolation,
                        speedMmPerSec, 500, 2500, TrajectoryProfileType.SCurve);

                    if (plan.TotalDurationSec <= 0)
                    {
                        _cmdX = _actX = 0;
                        _cmdY = _actY = 0;
                        _isHomed = true;
                        log = MakeLog("Info", "XY 平台已位于机械原点 (0.00, 0.00) mm，回零完成。");
                        task = Task.FromResult(true);
                    }
                    else
                    {
                        _activePlan = plan;
                        _elapsedMoveTimeSec = 0;
                        _isHomingMove = true;
                        _isHomed = false;
                        _psoEnabled = false;
                        _state = AxisState.Homing;
                        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _activeMoveTcs = tcs;
                        _activeMoveCancelReg = cancellationToken.Register(Stop);
                        log = MakeLog("Info", $"开始 XY 回零，当前 ({_cmdX:F1}, {_cmdY:F1}) → (0.0, 0.0) mm。");
                        task = tcs.Task;
                    }
                }
            }

            Emit(log);
            return task;
        }

        public Task<bool> MoveToAsync(
            Point2D target,
            XyMotionMode mode,
            double feedVelocityMmPerSec,
            double maxAccelMmPerSec2,
            double maxJerkMmPerSec3,
            TrajectoryProfileType profileType,
            double arcRadiusMm = 0,
            bool psoEnabled = false,
            double psoIntervalMm = 15.0,
            CancellationToken cancellationToken = default)
        {
            MotionLogEntry? log;
            Task<bool> task;

            lock (_sync)
            {
                if (!_isServoOn || _state != AxisState.Standstill)
                {
                    log = MakeLog("Warn", "XY 平台未处于待机状态 (Standstill)，无法执行运动。");
                    task = Task.FromResult(false);
                }
                else if (!_isHomed)
                {
                    log = MakeLog("Warn", "安全互锁：XY 平台尚未回零，请先执行 XY 回零。");
                    task = Task.FromResult(false);
                }
                else if (target.X < XMinMm || target.X > XMaxMm || target.Y < YMinMm || target.Y > YMaxMm)
                {
                    log = MakeLog("Warn", $"目标坐标 ({target.X:F1}, {target.Y:F1}) 超出工作区 [{XMinMm:F0}..{XMaxMm:F0}] × [{YMinMm:F0}..{YMaxMm:F0}] mm。");
                    task = Task.FromResult(false);
                }
                else
                {
                    var start = new Point2D(_cmdX, _cmdY);
                    var plan = XyTrajectoryPlanner.Plan(start, target, mode, feedVelocityMmPerSec, maxAccelMmPerSec2, maxJerkMmPerSec3, profileType, arcRadiusMm);

                    if (plan.TotalDurationSec <= 0)
                    {
                        log = MakeLog("Info", $"XY 已位于目标点 ({target.X:F1}, {target.Y:F1}) mm。");
                        task = Task.FromResult(true);
                    }
                    else if (!IsPlanWithinWorkspace(plan))
                    {
                        log = MakeLog("Warn", "规划的圆弧轨迹超出 XY 平台软限位范围 [0..200] × [0..150] mm，请增大半径或调整点位。");
                        task = Task.FromResult(false);
                    }
                    else
                    {
                        _activePlan = plan;
                        _elapsedMoveTimeSec = 0;
                        _isHomingMove = false;
                        _psoEnabled = psoEnabled && psoIntervalMm > 0 && mode != XyMotionMode.IndependentPtp;
                        _psoIntervalMm = psoIntervalMm;
                        _nextPsoDistanceMm = 0;
                        _state = AxisState.DiscreteMotion;

                        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _activeMoveTcs = tcs;
                        _activeMoveCancelReg = cancellationToken.Register(Stop);

                        var modeName = mode switch
                        {
                            XyMotionMode.IndependentPtp => "独立 PTP",
                            XyMotionMode.LinearInterpolation => "直线插补 G01",
                            XyMotionMode.CircularArcCw => $"顺圆插补 G02 (R={plan.ArcRadiusMm:F1})",
                            XyMotionMode.CircularArcCcw => $"逆圆插补 G03 (R={plan.ArcRadiusMm:F1})",
                            _ => mode.ToString(),
                        };
                        var psoTag = _psoEnabled ? $" [PSO 飞拍 Δs={psoIntervalMm:F1}mm]" : string.Empty;
                        log = MakeLog("Info", $"开始 {modeName}{psoTag}：({start.X:F1},{start.Y:F1}) → ({target.X:F1},{target.Y:F1})，路径 {plan.PathLengthMm:F1} mm，耗时 {plan.TotalDurationSec:F3} s。");
                        task = tcs.Task;
                    }
                }
            }

            Emit(log);
            return task;
        }

        /// <summary>Advances the XY stage physics by <paramref name="dtSec"/>. Public for deterministic unit testing.</summary>
        public void Step(double dtSec)
        {
            if (dtSec <= 0)
            {
                return;
            }

            MotionLogEntry? completionLog = null;
            TaskCompletionSource<bool>? completedTcs = null;
            CancellationTokenRegistration completedReg = default;

            lock (_sync)
            {
                if (_disposed || _state is AxisState.Disabled or AxisState.ErrorStop)
                {
                    return;
                }

                if (_state is AxisState.DiscreteMotion or AxisState.Homing && _activePlan is { } plan)
                {
                    _elapsedMoveTimeSec += dtSec;
                    var sample = plan.Evaluate(_elapsedMoveTimeSec);
                    _cmdX = sample.X;
                    _cmdY = sample.Y;
                    _vx = sample.Vx;
                    _vy = sample.Vy;

                    // Hardware-comparator style PSO trigger evaluation at exact path distances.
                    if (_psoEnabled)
                    {
                        var currentS = _elapsedMoveTimeSec >= plan.TotalDurationSec ? plan.PathLengthMm : sample.PathDistanceMm;
                        while (_nextPsoDistanceMm <= currentS + 1e-6 && _nextPsoDistanceMm <= plan.PathLengthMm + 1e-6)
                        {
                            var pt = plan.EvaluateAtPathDistance(Math.Min(_nextPsoDistanceMm, plan.PathLengthMm));
                            _triggerSeq++;
                            _triggers.Add(new TriggerMarker(pt, IsPso: true, _triggerSeq));
                            _nextPsoDistanceMm += _psoIntervalMm;
                        }
                    }

                    if (_elapsedMoveTimeSec >= plan.TotalDurationSec)
                    {
                        _elapsedMoveTimeSec = plan.TotalDurationSec;
                        _cmdX = plan.End.X;
                        _cmdY = plan.End.Y;
                        _vx = _vy = 0;

                        if (_isHomingMove)
                        {
                            _actX = _cmdX;
                            _actY = _cmdY;
                            _isHomed = true;
                            _isHomingMove = false;
                            completionLog = MakeLog("Info", "XY 平台回零完成，零点已建立于 (0.00, 0.00) mm。");
                        }

                        _state = AxisState.Standstill;
                        DetachActiveMoveLocked(out completedTcs, out completedReg);
                    }
                }

                // First-order dual-axis servo tracking lag.
                if (_isServoOn)
                {
                    var alpha = 1.0 - Math.Exp(-dtSec / ServoLagTimeConstantSec);
                    _actX += (_cmdX - _actX) * alpha;
                    _actY += (_cmdY - _actY) * alpha;
                    if (_state == AxisState.Standstill && double.Hypot(_cmdX - _actX, _cmdY - _actY) < 1e-3)
                    {
                        _actX = _cmdX;
                        _actY = _cmdY;
                    }

                    var last = _trail[^1];
                    if (double.Hypot(_actX - last.X, _actY - last.Y) >= TrailSampleSpacingMm || completedTcs is not null)
                    {
                        _trail.Add(new Point2D(_actX, _actY));
                        if (_trail.Count > MaxTrailPoints)
                        {
                            _trail.RemoveRange(0, _trail.Count - MaxTrailPoints);
                        }
                    }
                }
            }

            completedReg.Dispose();
            completedTcs?.TrySetResult(true);
            Emit(completionLog);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            TaskCompletionSource<bool>? tcs;
            CancellationTokenRegistration reg;
            lock (_sync)
            {
                _disposed = true;
                DetachActiveMoveLocked(out tcs, out reg);
            }

            reg.Dispose();
            tcs?.TrySetResult(false);
            _loopCts.Cancel();
            try
            {
                _loopTask?.Wait(TimeSpan.FromMilliseconds(200));
            }
            catch
            {
                // Ignore shutdown cancellation.
            }

            _loopCts.Dispose();
        }

        private bool IsPlanWithinWorkspace(XyTrajectoryPlan plan)
        {
            const double tol = 0.5;
            var samples = plan.SamplePolyline(48);
            foreach (var p in samples)
            {
                if (p.X < XMinMm - tol || p.X > XMaxMm + tol || p.Y < YMinMm - tol || p.Y > YMaxMm + tol)
                {
                    return false;
                }
            }

            return true;
        }

        private void DetachActiveMoveLocked(out TaskCompletionSource<bool>? tcs, out CancellationTokenRegistration reg)
        {
            tcs = _activeMoveTcs;
            reg = _activeMoveCancelReg;
            _activeMoveTcs = null;
            _activeMoveCancelReg = default;
        }

        private async Task RunPhysicsLoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TickIntervalSec));
            try
            {
                while (await timer.WaitForNextTickAsync(_loopCts.Token).ConfigureAwait(false))
                {
                    Step(TickIntervalSec);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        private static MotionLogEntry MakeLog(string level, string message) =>
            new(DateTime.Now, level, message);

        private void Emit(MotionLogEntry? entry)
        {
            if (entry is not null)
            {
                LogAdded?.Invoke(entry);
            }
        }
    }
}
