namespace LearnSmartManufacturing.Models.Prism
{
    #region 1. Modularity (IModule / IModuleCatalog / IModuleManager)

    public enum PrismInitMode
    {
        WhenAvailable,
        OnDemand,
    }

    public enum PrismModuleStatus
    {
        NotLoaded,
        Initialized,
        AutoResolvedDependency,
    }

    public sealed record PrismModuleSnapshot(
        string ModuleName,
        string DisplayName,
        PrismInitMode InitMode,
        IReadOnlyList<string> DependsOn,
        PrismModuleStatus Status,
        string LastActionNote);

    /// <summary>
    /// Simulates Prism's IModuleCatalog and IModuleManager with startup WhenAvailable loading,
    /// lazy OnDemand loading, and automatic topological dependency resolution.
    /// </summary>
    public sealed class SimulatedModuleCatalog
    {
        private sealed class ModuleNode
        {
            public required string Name { get; init; }
            public required string DisplayName { get; init; }
            public required PrismInitMode InitMode { get; init; }
            public required string[] DependsOn { get; init; }
            public PrismModuleStatus Status { get; set; }
            public string LastActionNote { get; set; } = string.Empty;
        }

        private readonly Dictionary<string, ModuleNode> _modules = new(StringComparer.Ordinal);

        public SimulatedModuleCatalog()
        {
            Reset();
        }

        public void Reset()
        {
            _modules.Clear();
            Add("CoreAuthModule", "核心权限 (CoreAuth)", PrismInitMode.WhenAvailable, []);
            Add("PlcCommModule", "PLC 通信 (PlcComm)", PrismInitMode.OnDemand, ["CoreAuthModule"]);
            Add("VisionModule", "视觉检测 (Vision)", PrismInitMode.OnDemand, ["CoreAuthModule"]);
            Add("MotionModule", "运动控制 (Motion)", PrismInitMode.OnDemand, ["PlcCommModule"]);
            Add("MesReportModule", "MES 上报 (MesReport)", PrismInitMode.OnDemand, ["PlcCommModule", "VisionModule"]);

            // Startup auto-loads WhenAvailable modules
            foreach (var mod in _modules.Values.Where(m => m.InitMode == PrismInitMode.WhenAvailable))
            {
                mod.Status = PrismModuleStatus.Initialized;
                mod.LastActionNote = "启动时自动 RegisterTypes → OnInitialized";
            }
        }

        public IReadOnlyList<string> LoadModule(string moduleName)
        {
            var log = new List<string>();
            if (!_modules.TryGetValue(moduleName, out var target))
            {
                log.Add($"[Error] 未在 IModuleCatalog 中找到模块 {moduleName}");
                return log;
            }

            if (target.Status != PrismModuleStatus.NotLoaded)
            {
                log.Add($"[Info] 模块 {target.Name} 已完成初始化，跳过重复加载");
                return log;
            }

            LoadRecursive(target, isRootTarget: true, log);
            return log;
        }

        public IReadOnlyList<PrismModuleSnapshot> GetSnapshots() =>
            _modules.Values.Select(m => new PrismModuleSnapshot(
                m.Name, m.DisplayName, m.InitMode, m.DependsOn, m.Status, m.LastActionNote)).ToList();

        private void LoadRecursive(ModuleNode mod, bool isRootTarget, List<string> log)
        {
            foreach (var depName in mod.DependsOn)
            {
                if (_modules.TryGetValue(depName, out var dep) && dep.Status == PrismModuleStatus.NotLoaded)
                {
                    LoadRecursive(dep, isRootTarget: false, log);
                }
            }

            if (mod.Status != PrismModuleStatus.NotLoaded) return;

            mod.Status = isRootTarget ? PrismModuleStatus.Initialized : PrismModuleStatus.AutoResolvedDependency;
            mod.LastActionNote = isRootTarget
                ? "按需 LoadModule → RegisterTypes → OnInitialized"
                : "作为前置依赖被自动拉起并初始化";

            var tag = isRootTarget ? "OnDemand" : "AutoDep";
            log.Add($"[{tag}] {mod.Name}: RegisterTypes(IContainerRegistry) → OnInitialized(IContainerProvider)");
        }

        private void Add(string name, string display, PrismInitMode mode, string[] deps)
        {
            _modules[name] = new ModuleNode
            {
                Name = name,
                DisplayName = display,
                InitMode = mode,
                DependsOn = deps,
                Status = PrismModuleStatus.NotLoaded,
                LastActionNote = mode == PrismInitMode.OnDemand ? "OnDemand 待命（未占用启动内存）" : string.Empty,
            };
        }
    }

    #endregion

    #region 2. EventAggregator (PubSubEvent<T> + ThreadOption + Filter + WeakReference)

    public enum PrismThreadOption
    {
        UIThread,
        BackgroundThread,
        PublisherThread,
    }

    public enum PrismAlarmSeverity
    {
        Info,
        Warning,
        Critical,
    }

    public sealed record PrismEventDeliveryRecord(
        string SubscriberName,
        PrismThreadOption ThreadOption,
        bool Delivered,
        string Reason,
        bool IsMemoryLeakRisk);

    public static class SimulatedEventAggregator
    {
        public static IReadOnlyList<PrismEventDeliveryRecord> PublishAlarm(
            PrismAlarmSeverity severity,
            PrismThreadOption uiSubscriberThreadOption,
            bool filterCriticalOnly,
            bool keepStrongReference)
        {
            var results = new List<PrismEventDeliveryRecord>();

            // Subscriber 1: UI Alarm Banner (uses configurable ThreadOption & Filter)
            var passedFilter = !filterCriticalOnly || severity == PrismAlarmSeverity.Critical;
            var uiReason = !passedFilter
                ? $"Filter(s => Critical) 拦截 ({severity})"
                : uiSubscriberThreadOption == PrismThreadOption.UIThread
                    ? $"Dispatcher.Invoke 切回主线程刷新 {severity}"
                    : uiSubscriberThreadOption == PrismThreadOption.BackgroundThread
                        ? $"Task.Run 后台执行（更新 UI 需跨线程封送!）"
                        : $"沿用发布者线程同步回调 ({severity})";

            results.Add(new("UI 报警横幅", uiSubscriberThreadOption, passedFilter, uiReason, keepStrongReference));

            // Subscriber 2: MES Audit Logger (BackgroundThread, receives Warning+)
            var mesPass = severity >= PrismAlarmSeverity.Warning;
            results.Add(new("MES 异步落盘", PrismThreadOption.BackgroundThread, mesPass,
                mesPass ? $"后台线程池写入 {severity} 审计记录" : "Filter 忽略 Info 级心跳",
                false));

            // Subscriber 3: EStop Interlock Handler (PublisherThread, Critical only)
            var estopPass = severity == PrismAlarmSeverity.Critical;
            results.Add(new("安全急停联锁", PrismThreadOption.PublisherThread, estopPass,
                estopPass ? "同线程零延迟触发断使能!" : "仅响应 Critical 急停事件",
                false));

            return results;
        }
    }

    #endregion

    #region 3. CompositeCommand & IDialogService Simulation

    public sealed record PrismCompositeChildState(string ModuleName, bool CanSave);

    public sealed record PrismCompositeExecutionResult(
        bool CanExecuteAll,
        string Summary,
        IReadOnlyList<string> ExecutedModules);

    public static class SimulatedCompositeCommand
    {
        public static PrismCompositeExecutionResult Evaluate(
            bool plcValid, bool visionValid, bool motionValid, bool executeNow)
        {
            var children = new PrismCompositeChildState[]
            {
                new("PLC 通信参数", plcValid),
                new("视觉检测配方", visionValid),
                new("运动控制点位", motionValid),
            };

            var blocked = children.Where(c => !c.CanSave).Select(c => c.ModuleName).ToList();
            var canExecute = blocked.Count == 0;

            if (!canExecute)
            {
                return new(false, $"CompositeCommand 禁用：[{string.Join(" / ", blocked)}] 校验未通过，全站保存被阻塞", []);
            }

            if (!executeNow)
            {
                return new(true, "CompositeCommand 就绪：3 个子模块 DelegateCommand 全部 CanExecute=true", []);
            }

            var executed = children.Select(c => $"{c.ModuleName}.SaveCommand.Execute() ✓").ToList();
            return new(true, "SaveAllCompositeCommand 已原子扇出执行 3 个子模块保存命令！", executed);
        }
    }

    public sealed record PrismDialogState(
        bool IsOpen,
        string DialogName,
        string IncomingParamsText,
        bool CanCloseGuardPassed,
        string LastCallbackResultText);

    #endregion
}
