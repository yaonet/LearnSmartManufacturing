using System.Globalization;

namespace LearnSmartManufacturing.Models.Patterns
{
    #region 4. State Pattern (Equipment Finite State Machine)

    public enum EquipmentStateKind
    {
        Idle,
        Running,
        Paused,
        Fault,
        Maintenance,
    }

    public enum EquipmentEventKind
    {
        Start,
        Pause,
        Resume,
        Stop,
        InjectFault,
        ResetAlarm,
        ToggleMaintenance,
    }

    public sealed record StateTransitionResult(
        EquipmentStateKind FromState,
        EquipmentStateKind ToState,
        EquipmentEventKind Event,
        bool IsAllowed,
        string Message);

    public interface IEquipmentState
    {
        EquipmentStateKind Kind { get; }
        string DisplayName { get; }
        string AllowedHint { get; }
        StateTransitionResult Handle(EquipmentEventKind evt);
    }

    public sealed class IdleState : IEquipmentState
    {
        public EquipmentStateKind Kind => EquipmentStateKind.Idle;
        public string DisplayName => "待机 (Idle)";
        public string AllowedHint => "允许：启动 / 进入维保 / 故障触发";

        public StateTransitionResult Handle(EquipmentEventKind evt) => evt switch
        {
            EquipmentEventKind.Start => new(Kind, EquipmentStateKind.Running, evt, true, "待机 → 自动运行：产线使能并启动节拍"),
            EquipmentEventKind.ToggleMaintenance => new(Kind, EquipmentStateKind.Maintenance, evt, true, "待机 → 维保点动：切入手动调试模式"),
            EquipmentEventKind.InjectFault => new(Kind, EquipmentStateKind.Fault, evt, true, "待机 → 故障报警：检测到急停/门禁触发"),
            _ => new(Kind, Kind, evt, false, $"Idle 状态拒绝事件 {evt}（请先启动或切入维保）"),
        };
    }

    public sealed class RunningState : IEquipmentState
    {
        public EquipmentStateKind Kind => EquipmentStateKind.Running;
        public string DisplayName => "自动运行 (Running)";
        public string AllowedHint => "允许：暂停 / 正常停机 / 急停故障（禁止直接进维保）";

        public StateTransitionResult Handle(EquipmentEventKind evt) => evt switch
        {
            EquipmentEventKind.Pause => new(Kind, EquipmentStateKind.Paused, evt, true, "自动运行 → 暂停：当前周期完成后保持位姿"),
            EquipmentEventKind.Stop => new(Kind, EquipmentStateKind.Idle, evt, true, "自动运行 → 待机：安全减速停机归位"),
            EquipmentEventKind.InjectFault => new(Kind, EquipmentStateKind.Fault, evt, true, "自动运行 → 故障报警：立即断使能并触发三色红灯！"),
            EquipmentEventKind.ToggleMaintenance => new(Kind, Kind, evt, false, "安全联锁拒绝：运行中严禁直接进入维保模式，请先停机！"),
            _ => new(Kind, Kind, evt, false, $"Running 状态忽略无效事件 {evt}"),
        };
    }

    public sealed class PausedState : IEquipmentState
    {
        public EquipmentStateKind Kind => EquipmentStateKind.Paused;
        public string DisplayName => "暂停 (Paused)";
        public string AllowedHint => "允许：恢复运行 / 停机 / 故障触发";

        public StateTransitionResult Handle(EquipmentEventKind evt) => evt switch
        {
            EquipmentEventKind.Resume or EquipmentEventKind.Start => new(Kind, EquipmentStateKind.Running, evt, true, "暂停 → 自动运行：恢复断点继续生产"),
            EquipmentEventKind.Stop => new(Kind, EquipmentStateKind.Idle, evt, true, "暂停 → 待机：清空当前周期并归位"),
            EquipmentEventKind.InjectFault => new(Kind, EquipmentStateKind.Fault, evt, true, "暂停 → 故障报警：安全回路断开"),
            _ => new(Kind, Kind, evt, false, $"Paused 状态拒绝事件 {evt}"),
        };
    }

    public sealed class FaultState : IEquipmentState
    {
        public EquipmentStateKind Kind => EquipmentStateKind.Fault;
        public string DisplayName => "故障报警 (Fault)";
        public string AllowedHint => "仅允许：报警复位 (ResetAlarm) 回到待机";

        public StateTransitionResult Handle(EquipmentEventKind evt) => evt switch
        {
            EquipmentEventKind.ResetAlarm => new(Kind, EquipmentStateKind.Idle, evt, true, "故障报警 → 待机：故障已确认并清除，恢复就绪"),
            EquipmentEventKind.Start or EquipmentEventKind.Resume => new(Kind, Kind, evt, false, "安全联锁拒绝：故障未复位，严禁直接启动设备！"),
            _ => new(Kind, Kind, evt, false, $"Fault 状态锁定，必须先执行 ResetAlarm（拒绝 {evt}）"),
        };
    }

    public sealed class MaintenanceState : IEquipmentState
    {
        public EquipmentStateKind Kind => EquipmentStateKind.Maintenance;
        public string DisplayName => "维保点动 (Maint)";
        public string AllowedHint => "允许：退出维保回待机 / 故障触发（禁止自动运行）";

        public StateTransitionResult Handle(EquipmentEventKind evt) => evt switch
        {
            EquipmentEventKind.ToggleMaintenance or EquipmentEventKind.Stop => new(Kind, EquipmentStateKind.Idle, evt, true, "维保点动 → 待机：退出点动并锁定安全门"),
            EquipmentEventKind.InjectFault => new(Kind, EquipmentStateKind.Fault, evt, true, "维保点动 → 故障报警：触发硬限位保护"),
            EquipmentEventKind.Start => new(Kind, Kind, evt, false, "安全联锁拒绝：维保模式下禁止一键自动运行，请先退出维保！"),
            _ => new(Kind, Kind, evt, false, $"Maintenance 状态拒绝事件 {evt}"),
        };
    }

    /// <summary>Context delegating transition logic strictly to the active IEquipmentState object.</summary>
    public sealed class EquipmentStateMachine
    {
        private readonly Dictionary<EquipmentStateKind, IEquipmentState> _states = new()
        {
            [EquipmentStateKind.Idle] = new IdleState(),
            [EquipmentStateKind.Running] = new RunningState(),
            [EquipmentStateKind.Paused] = new PausedState(),
            [EquipmentStateKind.Fault] = new FaultState(),
            [EquipmentStateKind.Maintenance] = new MaintenanceState(),
        };

        public IEquipmentState Current { get; private set; }
        public StateTransitionResult? LastTransition { get; private set; }

        public EquipmentStateMachine()
        {
            Current = _states[EquipmentStateKind.Idle];
        }

        public StateTransitionResult Fire(EquipmentEventKind evt)
        {
            var result = Current.Handle(evt);
            LastTransition = result;
            if (result.IsAllowed)
            {
                Current = _states[result.ToState];
            }

            return result;
        }
    }

    #endregion

    #region 5. Observer Pattern (Decoupled Equipment Event Fan-Out)

    public sealed record ObserverDispatchRecord(string SubscriberName, string ActionTaken, bool IsAlert);

    public interface IEquipmentObserver
    {
        string Name { get; }
        ObserverDispatchRecord OnStateChanged(StateTransitionResult transition);
    }

    public sealed class DelegateEquipmentObserver(string name, Func<StateTransitionResult, ObserverDispatchRecord> handler) : IEquipmentObserver
    {
        public string Name { get; } = name;
        public ObserverDispatchRecord OnStateChanged(StateTransitionResult transition) => handler(transition);
    }

    public sealed class EquipmentEventPublisher
    {
        private readonly List<IEquipmentObserver> _observers = [];

        public void Subscribe(IEquipmentObserver observer)
        {
            if (!_observers.Contains(observer)) _observers.Add(observer);
        }

        public void Unsubscribe(IEquipmentObserver observer) => _observers.Remove(observer);

        public IReadOnlyList<ObserverDispatchRecord> Publish(StateTransitionResult transition)
        {
            var list = new List<ObserverDispatchRecord>(_observers.Count);
            foreach (var obs in _observers)
            {
                list.Add(obs.OnStateChanged(transition));
            }

            return list;
        }
    }

    #endregion

    #region 6. Strategy Pattern (Hot-Swappable Vision Quality Decision Algorithms)

    public enum InspectionStrategyKind
    {
        StrictAutomotive,
        BalancedThreeSigma,
        HighSpeedSampling,
    }

    public sealed record StrategyBatchReport(
        InspectionStrategyKind Kind,
        string StrategyName,
        int BatchSize,
        int PassCount,
        int NgCount,
        double YieldPercent,
        double EscapeRiskPercent,
        double AvgCycleMs,
        string Summary);

    public interface IInspectionStrategy
    {
        InspectionStrategyKind Kind { get; }
        string Name { get; }
        StrategyBatchReport EvaluateBatch(ReadOnlySpan<double> defectScores);
    }

    public sealed class StrictAutomotiveStrategy : IInspectionStrategy
    {
        public InspectionStrategyKind Kind => InspectionStrategyKind.StrictAutomotive;
        public string Name => "车规零缺陷策略 (Strict · 阈值 0.25)";

        public StrategyBatchReport EvaluateBatch(ReadOnlySpan<double> defectScores)
        {
            int pass = 0, ng = 0;
            foreach (var s in defectScores)
            {
                if (s <= 0.25) pass++; else ng++;
            }

            var yield = defectScores.Length == 0 ? 0 : 100.0 * pass / defectScores.Length;
            return new(Kind, Name, defectScores.Length, pass, ng, Math.Round(yield, 1), 0.02, 42.5,
                string.Create(CultureInfo.InvariantCulture, $"多算法串联复检：漏检率压至 0.02%，代价是误杀偏高（良率 {yield:F1}%）与节拍较慢 (42.5 ms)"));
        }
    }

    public sealed class BalancedThreeSigmaStrategy : IInspectionStrategy
    {
        public InspectionStrategyKind Kind => InspectionStrategyKind.BalancedThreeSigma;
        public string Name => "标准 3σ 统计策略 (Balanced · 阈值 0.45)";

        public StrategyBatchReport EvaluateBatch(ReadOnlySpan<double> defectScores)
        {
            int pass = 0, ng = 0;
            foreach (var s in defectScores)
            {
                if (s <= 0.45) pass++; else ng++;
            }

            var yield = defectScores.Length == 0 ? 0 : 100.0 * pass / defectScores.Length;
            return new(Kind, Name, defectScores.Length, pass, ng, Math.Round(yield, 1), 0.18, 24.0,
                string.Create(CultureInfo.InvariantCulture, $"兼顾良率与漏检：良率 {yield:F1}%，平均单件 24.0 ms，适合 3C 常规产线"));
        }
    }

    public sealed class HighSpeedSamplingStrategy : IInspectionStrategy
    {
        public InspectionStrategyKind Kind => InspectionStrategyKind.HighSpeedSampling;
        public string Name => "高节拍快速抽检策略 (Fast · 阈值 0.65)";

        public StrategyBatchReport EvaluateBatch(ReadOnlySpan<double> defectScores)
        {
            int pass = 0, ng = 0;
            foreach (var s in defectScores)
            {
                if (s <= 0.65) pass++; else ng++;
            }

            var yield = defectScores.Length == 0 ? 0 : 100.0 * pass / defectScores.Length;
            return new(Kind, Name, defectScores.Length, pass, ng, Math.Round(yield, 1), 1.45, 9.8,
                string.Create(CultureInfo.InvariantCulture, $"仅跑轻量 ROI 阈值：节拍压缩至 9.8 ms (>100 件/秒)，但微瑕疵漏检风险上升至 1.45%"));
        }
    }

    public static class InspectionStrategyFactory
    {
        public static IInspectionStrategy Create(InspectionStrategyKind kind) => kind switch
        {
            InspectionStrategyKind.StrictAutomotive => new StrictAutomotiveStrategy(),
            InspectionStrategyKind.HighSpeedSampling => new HighSpeedSamplingStrategy(),
            _ => new BalancedThreeSigmaStrategy(),
        };

        /// <summary>Generates a deterministic 200-part defect-score sample batch for fair side-by-side comparison.</summary>
        public static double[] GenerateDeterministicBatch(int count = 200)
        {
            var scores = new double[count];
            for (var i = 0; i < count; i++)
            {
                // Realistic skewed industrial distribution: mostly clean (<0.25), some borderline (0.25..0.65), rare real defects (>0.65)
                var u = ((i * 37 + 13) % 100) / 100.0;
                scores[i] = u < 0.78 ? u * 0.28 : (u < 0.94 ? 0.26 + (u - 0.78) * 2.1 : 0.66 + (u - 0.94) * 5.0);
            }

            return scores;
        }
    }

    #endregion

    #region 7. Command Pattern (Reversible Recipe Edit with Undo / Redo Stacks)

    public sealed class RecipeTarget
    {
        public double TemperatureC { get; set; } = 185.0;
        public int ConveyorSpeedMmS { get; set; } = 450;
        public bool AutoRecheckEnabled { get; set; } = true;
    }

    public interface IRecipeCommand
    {
        string Description { get; }
        void Execute(RecipeTarget target);
        void Undo(RecipeTarget target);
    }

    public sealed class AdjustTemperatureCommand(double deltaC) : IRecipeCommand
    {
        private double _previous;
        public string Description => deltaC >= 0 ? $"炉温 +{deltaC:F0} °C" : $"炉温 {deltaC:F0} °C";

        public void Execute(RecipeTarget target)
        {
            _previous = target.TemperatureC;
            target.TemperatureC = Math.Clamp(target.TemperatureC + deltaC, 80, 320);
        }

        public void Undo(RecipeTarget target) => target.TemperatureC = _previous;
    }

    public sealed class AdjustSpeedCommand(int deltaMmS) : IRecipeCommand
    {
        private int _previous;
        public string Description => deltaMmS >= 0 ? $"传送带速度 +{deltaMmS} mm/s" : $"传送带速度 {deltaMmS} mm/s";

        public void Execute(RecipeTarget target)
        {
            _previous = target.ConveyorSpeedMmS;
            target.ConveyorSpeedMmS = Math.Clamp(target.ConveyorSpeedMmS + deltaMmS, 100, 1200);
        }

        public void Undo(RecipeTarget target) => target.ConveyorSpeedMmS = _previous;
    }

    public sealed class ToggleRecheckCommand : IRecipeCommand
    {
        private bool _previous;
        public string Description => "切换视觉自动复判开关";

        public void Execute(RecipeTarget target)
        {
            _previous = target.AutoRecheckEnabled;
            target.AutoRecheckEnabled = !target.AutoRecheckEnabled;
        }

        public void Undo(RecipeTarget target) => target.AutoRecheckEnabled = _previous;
    }

    public sealed class RecipeCommandManager
    {
        private readonly Stack<IRecipeCommand> _undoStack = new();
        private readonly Stack<IRecipeCommand> _redoStack = new();

        public RecipeTarget Target { get; } = new();
        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;
        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public string Execute(IRecipeCommand command)
        {
            command.Execute(Target);
            _undoStack.Push(command);
            _redoStack.Clear();
            return $"Execute: {command.Description}";
        }

        public string? Undo()
        {
            if (_undoStack.Count == 0) return null;
            var cmd = _undoStack.Pop();
            cmd.Undo(Target);
            _redoStack.Push(cmd);
            return $"Undo ↶: {cmd.Description}";
        }

        public string? Redo()
        {
            if (_redoStack.Count == 0) return null;
            var cmd = _redoStack.Pop();
            cmd.Execute(Target);
            _undoStack.Push(cmd);
            return $"Redo ↷: {cmd.Description}";
        }
    }

    #endregion
}
