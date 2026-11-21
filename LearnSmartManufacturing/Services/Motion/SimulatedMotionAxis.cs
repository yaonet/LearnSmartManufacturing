using LearnSmartManufacturing.Models.Motion;

namespace LearnSmartManufacturing.Services.Motion
{
    /// <summary>
    /// Deterministic single-axis servo simulator with a PLCopen/CiA 402 state machine,
    /// multi-stage homing, soft/hard limit interlocks, first-order servo tracking lag,
    /// and a 200 Hz background physics loop.
    /// </summary>
    public sealed class SimulatedMotionAxis : ISimulatedMotionAxis, IDisposable
    {
        private const double ServoLagTimeConstantSec = 0.0015;
        private const double InPositionToleranceMm = 0.01;
        private const double OriginSensorHalfWidthMm = 1.0;
        private const double OriginBackoffTargetMm = 3.0;
        private const double FollowingErrorLimitMm = 2.0;
        private const double TickIntervalSec = 0.005;

        private readonly Lock _sync = new();
        private readonly CancellationTokenSource _loopCts = new();
        private readonly Task? _loopTask;

        private AxisState _state = AxisState.Disabled;
        private bool _isServoOn;
        private bool _isHomed;
        private bool _injectedPel;
        private bool _injectedMel;
        private MotionAlarmCode _alarmCode = MotionAlarmCode.None;
        private string _alarmMessage = string.Empty;

        private double _commandPosMm = 45.0;
        private double _actualPosMm = 45.0;
        private double _velocityMmPerSec;
        private double _accelerationMmPerSec2;
        private double _jerkMmPerSec3;
        private double _pulsePerMm = 1000.0;

        // Active discrete move
        private TrajectoryPlan? _activePlan;
        private double _moveStartPosMm;
        private double _moveTargetPosMm = 45.0;
        private double _elapsedMoveTimeSec;
        private TaskCompletionSource<bool>? _activeMoveTcs;
        private CancellationTokenRegistration _activeMoveCancelReg;

        // Active JOG / Stopping
        private int _jogDirection;
        private double _jogTargetVel;
        private double _jogAccel = 400.0;
        private double _stopDecel = 600.0;

        // Active Homing
        private HomingStage _homingStage = HomingStage.None;
        private double _homeSearchVel;
        private double _homeCreepVel;
        private double _homeAccel;

        private bool _disposed;

        public SimulatedMotionAxis(string name = "X 轴 (模拟伺服)", bool startBackgroundLoop = true)
        {
            Name = name;
            if (startBackgroundLoop)
            {
                _loopTask = Task.Run(RunPhysicsLoopAsync);
            }
        }

        private enum HomingStage
        {
            None,
            SeekingNegativeLimit,
            SeekingOrigin,
            BackingOffOrigin,
            CreepingToZero,
        }

        public string Name { get; }
        public double SoftNegativeLimitMm => -20.0;
        public double SoftPositiveLimitMm => 200.0;
        public double HardNegativeLimitMm => -25.0;
        public double HardPositiveLimitMm => 210.0;

        public event Action<MotionLogEntry>? LogAdded;

        public AxisSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                var pel = _commandPosMm >= HardPositiveLimitMm || _injectedPel;
                var mel = _commandPosMm <= HardNegativeLimitMm || _injectedMel;
                var org = Math.Abs(_commandPosMm) <= OriginSensorHalfWidthMm;
                var isMoving = _state is AxisState.DiscreteMotion or AxisState.ContinuousMotion or AxisState.Homing or AxisState.Stopping;
                var inPos = _isServoOn && !isMoving && _alarmCode == MotionAlarmCode.None
                    && Math.Abs(_commandPosMm - _actualPosMm) <= InPositionToleranceMm;

                return new AxisSnapshot(
                    _state,
                    _isServoOn,
                    _isHomed,
                    inPos,
                    isMoving,
                    pel,
                    mel,
                    org,
                    _alarmCode,
                    _alarmMessage,
                    _commandPosMm,
                    _actualPosMm,
                    _velocityMmPerSec,
                    _accelerationMmPerSec2,
                    _jerkMmPerSec3,
                    _pulsePerMm,
                    _elapsedMoveTimeSec,
                    _moveTargetPosMm,
                    _activePlan);
            }
        }

        public void SetPulsePerMm(double pulsePerMm)
        {
            if (!double.IsFinite(pulsePerMm) || pulsePerMm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pulsePerMm), "Pulse/mm must be positive.");
            }

            lock (_sync)
            {
                _pulsePerMm = pulsePerMm;
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
                    log = MakeLog("Warn", "存在未清除报警，请先执行“清报警”后再使能伺服。");
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
                    log = MakeLog("Info", "伺服使能成功，轴进入待机状态 (Standstill)。");
                }
            }

            Emit(log);
            return ok;
        }

        public void ServoOff()
        {
            MotionLogEntry? log = null;
            TaskCompletionSource<bool>? toComplete = null;
            CancellationTokenRegistration cancelReg = default;

            lock (_sync)
            {
                if (!_isServoOn && _state == AxisState.Disabled)
                {
                    return;
                }

                DetachActiveMoveLocked(out toComplete, out cancelReg);
                _isServoOn = false;
                _velocityMmPerSec = 0;
                _accelerationMmPerSec2 = 0;
                _jerkMmPerSec3 = 0;
                _jogDirection = 0;
                _homingStage = HomingStage.None;
                if (_state != AxisState.ErrorStop)
                {
                    _state = AxisState.Disabled;
                }

                log = MakeLog("Info", "伺服已断使能 (Disabled)。");
            }

            cancelReg.Dispose();
            toComplete?.TrySetResult(false);
            Emit(log);
        }

        public void ResetAlarm()
        {
            MotionLogEntry? log;
            lock (_sync)
            {
                _alarmCode = MotionAlarmCode.None;
                _alarmMessage = string.Empty;
                _injectedPel = false;
                _injectedMel = false;
                _actualPosMm = _commandPosMm;
                _velocityMmPerSec = 0;
                _accelerationMmPerSec2 = 0;
                _jerkMmPerSec3 = 0;
                _isServoOn = false;
                _state = AxisState.Disabled;
                log = MakeLog("Info", "报警已复位，轴处于未使能状态 (Disabled)，请重新使能。");
            }

            Emit(log);
        }

        public void EmergencyStop()
        {
            MotionLogEntry? log;
            TaskCompletionSource<bool>? toComplete;
            CancellationTokenRegistration cancelReg;

            lock (_sync)
            {
                DetachActiveMoveLocked(out toComplete, out cancelReg);
                _isServoOn = false;
                _isHomed = false;
                _velocityMmPerSec = 0;
                _accelerationMmPerSec2 = 0;
                _jerkMmPerSec3 = 0;
                _jogDirection = 0;
                _homingStage = HomingStage.None;
                _alarmCode = MotionAlarmCode.EmergencyStop;
                _alarmMessage = "急停 (E-Stop) 触发，已切断使能并失效零点";
                _state = AxisState.ErrorStop;
                log = MakeLog("Error", "急停触发 (ErrorStop)：已立即切断使能并清除回零标志，复位后需重新使能并回零。");
            }

            cancelReg.Dispose();
            toComplete?.TrySetResult(false);
            Emit(log);
        }

        public void Stop()
        {
            MotionLogEntry? log = null;
            TaskCompletionSource<bool>? toComplete = null;
            CancellationTokenRegistration cancelReg = default;

            lock (_sync)
            {
                if (_state is AxisState.Disabled or AxisState.Standstill or AxisState.ErrorStop)
                {
                    return;
                }

                DetachActiveMoveLocked(out toComplete, out cancelReg);
                _jogDirection = 0;
                _homingStage = HomingStage.None;
                _stopDecel = Math.Max(400.0, Math.Abs(_accelerationMmPerSec2) * 1.5);
                if (Math.Abs(_velocityMmPerSec) > 1e-4)
                {
                    _state = AxisState.Stopping;
                    log = MakeLog("Info", "收到正常停止指令，轴正在平滑减速停机 (Stopping)。");
                }
                else
                {
                    _velocityMmPerSec = 0;
                    _accelerationMmPerSec2 = 0;
                    _jerkMmPerSec3 = 0;
                    _state = _isServoOn ? AxisState.Standstill : AxisState.Disabled;
                    log = MakeLog("Info", "轴已停止 (Standstill)。");
                }
            }

            cancelReg.Dispose();
            toComplete?.TrySetResult(false);
            Emit(log);
        }

        public bool StartJog(int direction, double velocityMmPerSec, double accelerationMmPerSec2)
        {
            if (direction is not (1 or -1))
            {
                throw new ArgumentOutOfRangeException(nameof(direction), "JOG direction must be +1 or -1.");
            }

            if (!double.IsFinite(velocityMmPerSec) || velocityMmPerSec <= 0 || !double.IsFinite(accelerationMmPerSec2) || accelerationMmPerSec2 <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(velocityMmPerSec), "JOG velocity and acceleration must be positive.");
            }

            MotionLogEntry? log;
            bool started = false;
            lock (_sync)
            {
                if (!_isServoOn || _state == AxisState.ErrorStop)
                {
                    log = MakeLog("Warn", "轴未使能或处于报警状态，无法执行 JOG 点动。");
                }
                else if (_state is not (AxisState.Standstill or AxisState.ContinuousMotion))
                {
                    log = MakeLog("Warn", $"当前状态 {_state} 下禁止启动 JOG 点动。");
                }
                else if (direction > 0 && (_commandPosMm >= HardPositiveLimitMm || _injectedPel))
                {
                    log = MakeLog("Warn", "正限位 (+EL) 触发中，禁止正向点动，请使用 JOG- 反向脱困。");
                }
                else if (direction < 0 && (_commandPosMm <= HardNegativeLimitMm || _injectedMel))
                {
                    log = MakeLog("Warn", "负限位 (-EL) 触发中，禁止负向点动，请使用 JOG+ 正向脱困。");
                }
                else
                {
                    _jogDirection = direction;
                    _jogTargetVel = velocityMmPerSec;
                    _jogAccel = accelerationMmPerSec2;
                    _state = AxisState.ContinuousMotion;
                    started = true;
                    log = MakeLog("Info", $"开始 {(direction > 0 ? "JOG+" : "JOG-")} 点动 (目标速度 {velocityMmPerSec:F1} mm/s)。");
                }
            }

            Emit(log);
            return started;
        }

        public void StopJog()
        {
            MotionLogEntry? log = null;
            lock (_sync)
            {
                if (_state != AxisState.ContinuousMotion)
                {
                    return;
                }

                _jogDirection = 0;
                _stopDecel = _jogAccel;
                _state = Math.Abs(_velocityMmPerSec) > 1e-4 ? AxisState.Stopping : AxisState.Standstill;
                log = MakeLog("Info", "释放 JOG，轴平滑减速停止。");
            }

            Emit(log);
        }

        public Task<bool> HomeAsync(
            HomingMode mode,
            double searchVelocityMmPerSec,
            double creepVelocityMmPerSec,
            double accelerationMmPerSec2,
            CancellationToken cancellationToken = default)
        {
            if (searchVelocityMmPerSec <= 0 || creepVelocityMmPerSec <= 0 || accelerationMmPerSec2 <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(searchVelocityMmPerSec), "Homing velocities and acceleration must be positive.");
            }

            MotionLogEntry? log;
            TaskCompletionSource<bool>? tcs = null;
            lock (_sync)
            {
                if (!_isServoOn || _state != AxisState.Standstill)
                {
                    log = MakeLog("Warn", "仅在使能待机 (Standstill) 状态下允许执行回零。");
                }
                else
                {
                    _isHomed = false;
                    _activePlan = null;
                    _homeSearchVel = searchVelocityMmPerSec;
                    _homeCreepVel = creepVelocityMmPerSec;
                    _homeAccel = accelerationMmPerSec2;
                    _homingStage = mode == HomingMode.NegativeLimitThenOrigin
                        ? HomingStage.SeekingNegativeLimit
                        : HomingStage.SeekingOrigin;
                    _state = AxisState.Homing;
                    _moveTargetPosMm = 0;
                    tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _activeMoveTcs = tcs;
                    _activeMoveCancelReg = cancellationToken.Register(Stop);

                    var modeText = mode == HomingMode.NegativeLimitThenOrigin ? "寻负限位→原点精定位" : "寻原点开关精定位";
                    log = MakeLog("Info", $"开始回零 ({modeText})，搜索速度 {searchVelocityMmPerSec:F0} mm/s，爬行速度 {creepVelocityMmPerSec:F0} mm/s。");
                }
            }

            Emit(log);
            return tcs is not null ? tcs.Task : Task.FromResult(false);
        }

        public Task<bool> MoveAbsoluteAsync(
            double targetPositionMm,
            double maxVelocityMmPerSec,
            double maxAccelerationMmPerSec2,
            double maxJerkMmPerSec3,
            TrajectoryProfileType profileType,
            CancellationToken cancellationToken = default)
        {
            MotionLogEntry? log;
            Task<bool> task;

            lock (_sync)
            {
                if (!_isServoOn || _state != AxisState.Standstill)
                {
                    log = MakeLog("Warn", "轴未处于使能待机状态 (Standstill)，无法启动绝对定位。");
                    task = Task.FromResult(false);
                }
                else if (!_isHomed)
                {
                    log = MakeLog("Warn", "安全互锁：轴尚未回零 (IsHomed = False)，禁止执行绝对定位，请先回零。");
                    task = Task.FromResult(false);
                }
                else if (targetPositionMm < SoftNegativeLimitMm || targetPositionMm > SoftPositiveLimitMm)
                {
                    log = MakeLog("Warn", $"目标位置 {targetPositionMm:F2} mm 超出软限位 [{SoftNegativeLimitMm:F0}, {SoftPositiveLimitMm:F0}] mm。");
                    task = Task.FromResult(false);
                }
                else
                {
                    var delta = targetPositionMm - _commandPosMm;
                    task = StartPlannedMoveLocked(delta, targetPositionMm, maxVelocityMmPerSec, maxAccelerationMmPerSec2, maxJerkMmPerSec3, profileType, "绝对定位", cancellationToken, out log);
                }
            }

            Emit(log);
            return task;
        }

        public Task<bool> MoveRelativeAsync(
            double deltaMm,
            double maxVelocityMmPerSec,
            double maxAccelerationMmPerSec2,
            double maxJerkMmPerSec3,
            TrajectoryProfileType profileType,
            CancellationToken cancellationToken = default)
        {
            MotionLogEntry? log;
            Task<bool> task;

            lock (_sync)
            {
                if (!_isServoOn || _state != AxisState.Standstill)
                {
                    log = MakeLog("Warn", "轴未处于使能待机状态 (Standstill)，无法启动相对定位。");
                    task = Task.FromResult(false);
                }
                else if (deltaMm > 0 && (_commandPosMm >= HardPositiveLimitMm || _injectedPel))
                {
                    log = MakeLog("Warn", "正限位 (+EL) 触发中，禁止正向移动。");
                    task = Task.FromResult(false);
                }
                else if (deltaMm < 0 && (_commandPosMm <= HardNegativeLimitMm || _injectedMel))
                {
                    log = MakeLog("Warn", "负限位 (-EL) 触发中，禁止负向移动。");
                    task = Task.FromResult(false);
                }
                else
                {
                    var target = _commandPosMm + deltaMm;
                    if (_isHomed && (target < SoftNegativeLimitMm || target > SoftPositiveLimitMm))
                    {
                        log = MakeLog("Warn", $"目标位置 {target:F2} mm 超出软限位 [{SoftNegativeLimitMm:F0}, {SoftPositiveLimitMm:F0}] mm。");
                        task = Task.FromResult(false);
                    }
                    else
                    {
                        task = StartPlannedMoveLocked(deltaMm, target, maxVelocityMmPerSec, maxAccelerationMmPerSec2, maxJerkMmPerSec3, profileType, "相对定位", cancellationToken, out log);
                    }
                }
            }

            Emit(log);
            return task;
        }

        public void InjectFault(MotionAlarmCode alarmCode)
        {
            if (alarmCode == MotionAlarmCode.None)
            {
                return;
            }

            if (alarmCode == MotionAlarmCode.EmergencyStop)
            {
                EmergencyStop();
                return;
            }

            MotionLogEntry? log;
            TaskCompletionSource<bool>? toComplete;
            CancellationTokenRegistration cancelReg;

            lock (_sync)
            {
                DetachActiveMoveLocked(out toComplete, out cancelReg);
                _velocityMmPerSec = 0;
                _accelerationMmPerSec2 = 0;
                _jerkMmPerSec3 = 0;
                _jogDirection = 0;
                _homingStage = HomingStage.None;
                _isServoOn = false;
                _alarmCode = alarmCode;
                _state = AxisState.ErrorStop;

                switch (alarmCode)
                {
                    case MotionAlarmCode.PositiveLimit:
                        _injectedPel = true;
                        _alarmMessage = "正硬限位 (+EL) 故障注入";
                        break;
                    case MotionAlarmCode.NegativeLimit:
                        _injectedMel = true;
                        _alarmMessage = "负硬限位 (-EL) 故障注入";
                        break;
                    case MotionAlarmCode.FollowingErrorExceeded:
                        _actualPosMm = _commandPosMm - 3.2;
                        _alarmMessage = $"跟随误差超限 (|e| > {FollowingErrorLimitMm:F1} mm)";
                        break;
                    case MotionAlarmCode.ServoFault:
                        _alarmMessage = "伺服驱动器内部报警 (AL-16 过载)";
                        break;
                }

                log = MakeLog("Error", $"故障触发 (ErrorStop)：{_alarmMessage}，请清报警后重新使能。");
            }

            cancelReg.Dispose();
            toComplete?.TrySetResult(false);
            Emit(log);
        }

        /// <summary>Advances the axis simulation by <paramref name="dtSec"/>. Public for deterministic testing.</summary>
        public void Step(double dtSec)
        {
            if (dtSec <= 0)
            {
                return;
            }

            List<MotionLogEntry>? pendingLogs = null;
            TaskCompletionSource<bool>? completedTcs = null;
            bool completedResult = false;
            CancellationTokenRegistration completedCancelReg = default;

            void QueueLog(string level, string msg)
            {
                pendingLogs ??= [];
                pendingLogs.Add(MakeLog(level, msg));
            }

            lock (_sync)
            {
                if (_disposed || _state is AxisState.Disabled or AxisState.ErrorStop)
                {
                    return;
                }

                switch (_state)
                {
                    case AxisState.DiscreteMotion:
                        StepDiscreteMotionLocked(dtSec, QueueLog, out completedTcs, out completedResult, out completedCancelReg);
                        break;

                    case AxisState.ContinuousMotion:
                        StepJogLocked(dtSec, QueueLog);
                        break;

                    case AxisState.Stopping:
                        StepStoppingLocked(dtSec, QueueLog);
                        break;

                    case AxisState.Homing:
                        StepHomingLocked(dtSec, QueueLog, out completedTcs, out completedResult, out completedCancelReg);
                        break;
                }

                // First-order servo tracking dynamics.
                if (_isServoOn && _state != AxisState.ErrorStop)
                {
                    var alpha = 1.0 - Math.Exp(-dtSec / ServoLagTimeConstantSec);
                    _actualPosMm += (_commandPosMm - _actualPosMm) * alpha;
                    if (_state == AxisState.Standstill && Math.Abs(_commandPosMm - _actualPosMm) < 1e-4)
                    {
                        _actualPosMm = _commandPosMm;
                    }

                    if (Math.Abs(_commandPosMm - _actualPosMm) > FollowingErrorLimitMm)
                    {
                        TripAlarmLocked(MotionAlarmCode.FollowingErrorExceeded,
                            $"跟随误差超限 ({_commandPosMm - _actualPosMm:F3} mm)",
                            QueueLog, out completedTcs, out completedCancelReg);
                        completedResult = false;
                    }
                }
            }

            completedCancelReg.Dispose();
            completedTcs?.TrySetResult(completedResult);
            if (pendingLogs is not null)
            {
                foreach (var entry in pendingLogs)
                {
                    Emit(entry);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            TaskCompletionSource<bool>? toComplete;
            CancellationTokenRegistration cancelReg;
            lock (_sync)
            {
                _disposed = true;
                DetachActiveMoveLocked(out toComplete, out cancelReg);
            }

            cancelReg.Dispose();
            toComplete?.TrySetResult(false);
            _loopCts.Cancel();
            try
            {
                _loopTask?.Wait(TimeSpan.FromMilliseconds(200));
            }
            catch
            {
                // Ignore cancellation during shutdown.
            }

            _loopCts.Dispose();
        }

        private Task<bool> StartPlannedMoveLocked(
            double deltaMm,
            double targetPosMm,
            double maxVel,
            double maxAcc,
            double maxJerk,
            TrajectoryProfileType profileType,
            string modeLabel,
            CancellationToken cancellationToken,
            out MotionLogEntry? log)
        {
            var plan = MotionTrajectoryPlanner.Plan(new MotionProfileParameters(deltaMm, maxVel, maxAcc, maxJerk, profileType));
            _activePlan = plan;
            _moveStartPosMm = _commandPosMm;
            _moveTargetPosMm = targetPosMm;
            _elapsedMoveTimeSec = 0;

            if (plan.TotalDurationSec <= 0)
            {
                log = MakeLog("Info", $"{modeLabel}目标与当前位置一致 ({targetPosMm:F2} mm)，无需移动。");
                return Task.FromResult(true);
            }

            _state = AxisState.DiscreteMotion;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeMoveTcs = tcs;
            _activeMoveCancelReg = cancellationToken.Register(Stop);

            var profileLabel = profileType == TrajectoryProfileType.SCurve ? "S型曲线" : "T型曲线";
            log = MakeLog("Info",
                $"开始{modeLabel} ({profileLabel})：目标 {targetPosMm:F2} mm (Δ={deltaMm:+0.00;-0.00;0.00} mm)，预计耗时 {plan.TotalDurationSec:F3} s。");
            return tcs.Task;
        }

        private void StepDiscreteMotionLocked(
            double dtSec,
            Action<string, string> queueLog,
            out TaskCompletionSource<bool>? completedTcs,
            out bool completedResult,
            out CancellationTokenRegistration cancelReg)
        {
            completedTcs = null;
            completedResult = false;
            cancelReg = default;

            if (_activePlan is not { } plan)
            {
                _state = AxisState.Standstill;
                return;
            }

            _elapsedMoveTimeSec += dtSec;
            var sample = plan.Evaluate(_elapsedMoveTimeSec);
            _commandPosMm = _moveStartPosMm + sample.PositionMm;
            _velocityMmPerSec = sample.VelocityMmPerSec;
            _accelerationMmPerSec2 = sample.AccelerationMmPerSec2;
            _jerkMmPerSec3 = sample.JerkMmPerSec3;

            if (CheckHardLimitTripLocked(queueLog, out completedTcs, out cancelReg))
            {
                completedResult = false;
                return;
            }

            if (_elapsedMoveTimeSec >= plan.TotalDurationSec)
            {
                _elapsedMoveTimeSec = plan.TotalDurationSec;
                _commandPosMm = _moveTargetPosMm;
                _velocityMmPerSec = 0;
                _accelerationMmPerSec2 = 0;
                _jerkMmPerSec3 = 0;
                _state = AxisState.Standstill;
                DetachActiveMoveLocked(out completedTcs, out cancelReg);
                completedResult = true;
                queueLog("Info", $"定位到位：当前位置 {_commandPosMm:F3} mm (实际耗时 {plan.TotalDurationSec:F3} s)。");
            }
        }

        private void StepJogLocked(double dtSec, Action<string, string> queueLog)
        {
            var targetVel = _jogDirection * _jogTargetVel;
            var dv = targetVel - _velocityMmPerSec;
            var maxDv = _jogAccel * dtSec;
            if (Math.Abs(dv) <= maxDv)
            {
                _velocityMmPerSec = targetVel;
                _accelerationMmPerSec2 = 0;
            }
            else
            {
                var sign = Math.Sign(dv);
                _velocityMmPerSec += sign * maxDv;
                _accelerationMmPerSec2 = sign * _jogAccel;
            }

            _jerkMmPerSec3 = 0;
            _commandPosMm += _velocityMmPerSec * dtSec;
            _moveTargetPosMm = _commandPosMm;

            CheckHardLimitTripLocked(queueLog, out _, out _);
        }

        private void StepStoppingLocked(double dtSec, Action<string, string> queueLog)
        {
            var maxDv = _stopDecel * dtSec;
            if (Math.Abs(_velocityMmPerSec) <= maxDv)
            {
                _velocityMmPerSec = 0;
                _accelerationMmPerSec2 = 0;
                _jerkMmPerSec3 = 0;
                _state = _isServoOn ? AxisState.Standstill : AxisState.Disabled;
                queueLog("Info", $"减速停止完成，当前位置 {_commandPosMm:F3} mm。");
                return;
            }

            var decelSign = -Math.Sign(_velocityMmPerSec);
            _accelerationMmPerSec2 = decelSign * _stopDecel;
            _velocityMmPerSec += _accelerationMmPerSec2 * dtSec;
            _jerkMmPerSec3 = 0;
            _commandPosMm += _velocityMmPerSec * dtSec;
            _moveTargetPosMm = _commandPosMm;

            CheckHardLimitTripLocked(queueLog, out _, out _);
        }

        private void StepHomingLocked(
            double dtSec,
            Action<string, string> queueLog,
            out TaskCompletionSource<bool>? completedTcs,
            out bool completedResult,
            out CancellationTokenRegistration cancelReg)
        {
            completedTcs = null;
            completedResult = false;
            cancelReg = default;

            void ApproachVelocity(double desiredVel)
            {
                var dv = desiredVel - _velocityMmPerSec;
                var maxDv = _homeAccel * dtSec;
                if (Math.Abs(dv) <= maxDv)
                {
                    _velocityMmPerSec = desiredVel;
                    _accelerationMmPerSec2 = 0;
                }
                else
                {
                    var s = Math.Sign(dv);
                    _velocityMmPerSec += s * maxDv;
                    _accelerationMmPerSec2 = s * _homeAccel;
                }

                _jerkMmPerSec3 = 0;
                _commandPosMm += _velocityMmPerSec * dtSec;
            }

            switch (_homingStage)
            {
                case HomingStage.SeekingNegativeLimit:
                    ApproachVelocity(-_homeSearchVel);
                    if (_commandPosMm <= HardNegativeLimitMm)
                    {
                        _commandPosMm = HardNegativeLimitMm;
                        _velocityMmPerSec = 0;
                        _accelerationMmPerSec2 = 0;
                        _homingStage = HomingStage.SeekingOrigin;
                        queueLog("Info", "回零阶段 1/3：已触碰负限位 (-EL)，反向搜寻原点开关 (ORG)。");
                    }

                    break;

                case HomingStage.SeekingOrigin:
                    var seekDir = _commandPosMm < 0 ? 1 : -1;
                    ApproachVelocity(seekDir * _homeSearchVel);
                    if (Math.Abs(_commandPosMm) <= OriginSensorHalfWidthMm)
                    {
                        _velocityMmPerSec = 0;
                        _accelerationMmPerSec2 = 0;
                        _homingStage = HomingStage.BackingOffOrigin;
                        queueLog("Info", "回零阶段 2/3：已触发原点开关 (ORG)，脱离开关准备低速精定位。");
                    }

                    break;

                case HomingStage.BackingOffOrigin:
                    ApproachVelocity(Math.Max(_homeCreepVel * 2.0, _homeSearchVel * 0.4));
                    if (_commandPosMm >= OriginBackoffTargetMm)
                    {
                        _commandPosMm = OriginBackoffTargetMm;
                        _velocityMmPerSec = 0;
                        _accelerationMmPerSec2 = 0;
                        _homingStage = HomingStage.CreepingToZero;
                        queueLog("Info", "回零阶段 3/3：低速爬行逼近机械零点 (0.000 mm)。");
                    }

                    break;

                case HomingStage.CreepingToZero:
                    ApproachVelocity(-_homeCreepVel);
                    if (_commandPosMm <= 0)
                    {
                        _commandPosMm = 0;
                        _actualPosMm = 0;
                        _velocityMmPerSec = 0;
                        _accelerationMmPerSec2 = 0;
                        _jerkMmPerSec3 = 0;
                        _moveTargetPosMm = 0;
                        _isHomed = true;
                        _homingStage = HomingStage.None;
                        _state = AxisState.Standstill;
                        DetachActiveMoveLocked(out completedTcs, out cancelReg);
                        completedResult = true;
                        queueLog("Info", "回零完成 (IsHomed = True)，机械零点已建立于 0.000 mm。");
                    }

                    break;
            }
        }

        private bool CheckHardLimitTripLocked(
            Action<string, string> queueLog,
            out TaskCompletionSource<bool>? completedTcs,
            out CancellationTokenRegistration cancelReg)
        {
            completedTcs = null;
            cancelReg = default;

            if (_commandPosMm >= HardPositiveLimitMm && _velocityMmPerSec > 0)
            {
                _commandPosMm = HardPositiveLimitMm;
                TripAlarmLocked(MotionAlarmCode.PositiveLimit,
                    $"触发正硬限位 (+EL @ {HardPositiveLimitMm:F0} mm)", queueLog, out completedTcs, out cancelReg);
                return true;
            }

            if (_commandPosMm <= HardNegativeLimitMm && _velocityMmPerSec < 0)
            {
                _commandPosMm = HardNegativeLimitMm;
                TripAlarmLocked(MotionAlarmCode.NegativeLimit,
                    $"触发负硬限位 (-EL @ {HardNegativeLimitMm:F0} mm)", queueLog, out completedTcs, out cancelReg);
                return true;
            }

            return false;
        }

        private void TripAlarmLocked(
            MotionAlarmCode code,
            string message,
            Action<string, string> queueLog,
            out TaskCompletionSource<bool>? completedTcs,
            out CancellationTokenRegistration cancelReg)
        {
            DetachActiveMoveLocked(out completedTcs, out cancelReg);
            _velocityMmPerSec = 0;
            _accelerationMmPerSec2 = 0;
            _jerkMmPerSec3 = 0;
            _jogDirection = 0;
            _homingStage = HomingStage.None;
            _isServoOn = false;
            _alarmCode = code;
            _alarmMessage = message;
            _state = AxisState.ErrorStop;
            queueLog("Error", $"报警停机 (ErrorStop)：{message}。");
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
