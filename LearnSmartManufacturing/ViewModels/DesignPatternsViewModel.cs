using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.Patterns;

namespace LearnSmartManufacturing.ViewModels
{
    public sealed record PlcVendorOption(PlcVendorKind Vendor, string Name);
    public sealed record StrategyOption(InspectionStrategyKind Kind, string Name);
    public sealed record PatternCategoryOption(PatternCategory Category, string Name);

    public partial class DesignPatternsViewModel : ObservableObject
    {
        private readonly EquipmentStateMachine _fsm = new();
        private readonly EquipmentEventPublisher _publisher = new();
        private readonly RecipeCommandManager _cmdManager = new();
        private readonly double[] _sampleBatch = InspectionStrategyFactory.GenerateDeterministicBatch(200);

        private readonly IEquipmentObserver _uiObserver;
        private readonly IEquipmentObserver _towerLightObserver;
        private readonly IEquipmentObserver _auditDbObserver;
        private readonly IEquipmentObserver _mesObserver;

        private int _workpieceSeq = 1001;

        public DesignPatternsViewModel()
        {
            VendorOptions =
            [
                new(PlcVendorKind.SiemensS7, "西门子 S7 (S7Comm · 102)"),
                new(PlcVendorKind.MitsubishiMc, "三菱 MC (SLMP 3E · 5000)"),
                new(PlcVendorKind.OmronFins, "欧姆龙 FINS (TCP · 9600)"),
            ];

            StrategyOptions =
            [
                new(InspectionStrategyKind.StrictAutomotive, "车规零缺陷策略 (Strict · 0.25)"),
                new(InspectionStrategyKind.BalancedThreeSigma, "标准 3σ 统计策略 (Balanced · 0.45)"),
                new(InspectionStrategyKind.HighSpeedSampling, "高节拍快速抽检 (Fast · 0.65)"),
            ];

            CategoryFilters =
            [
                new(PatternCategory.All, "全部模式 (10)"),
                new(PatternCategory.Creational, "创建型 (2)"),
                new(PatternCategory.Structural, "结构型 (3)"),
                new(PatternCategory.Behavioral, "行为型 (5)"),
            ];

            _uiObserver = new DelegateEquipmentObserver("UI 状态看板", tr =>
                new("UI 状态看板", tr.IsAllowed ? $"刷新状态 → {tr.ToState}" : "弹窗提示安全联锁拒绝", !tr.IsAllowed || tr.ToState == EquipmentStateKind.Fault));

            _towerLightObserver = new DelegateEquipmentObserver("三色声光灯", tr =>
            {
                var action = tr.ToState switch
                {
                    EquipmentStateKind.Running => "绿灯常亮",
                    EquipmentStateKind.Paused => "黄灯闪烁",
                    EquipmentStateKind.Fault => "红灯爆闪 + 蜂鸣器!",
                    EquipmentStateKind.Maintenance => "蓝/黄交替",
                    _ => "绿灯待机呼吸",
                };
                return new("三色声光灯", tr.IsAllowed ? action : "保持当前灯态", tr.ToState == EquipmentStateKind.Fault);
            });

            _auditDbObserver = new DelegateEquipmentObserver("SQLite 审计库", tr =>
                new("SQLite 审计库", $"写入 {tr.FromState}→{tr.ToState} ({tr.Event})", !tr.IsAllowed));

            _mesObserver = new DelegateEquipmentObserver("MES 异常上报", tr =>
                new("MES 异常上报",
                    tr.ToState == EquipmentStateKind.Fault ? "推送 EStop/停机工单!" : $"同步设备态 {tr.ToState}",
                    tr.ToState == EquipmentStateKind.Fault));

            SyncObservers();

            SelectedVendor = VendorOptions[0];
            SelectedStrategy = StrategyOptions[1]; // Balanced 3-Sigma
            SelectedCategory = CategoryFilters[0]; // All 10

            // Initialize Tab 1 live pipeline run (2 transient failures auto-recovered by RetryDecorator)
            RunPipeline();

            // Initialize Tab 2 FSM & Observer standby snapshot
            SyncFsmState();
            ObserverDispatches = _publisher.Publish(new StateTransitionResult(
                EquipmentStateKind.Idle, EquipmentStateKind.Idle, EquipmentEventKind.Stop, true, "系统上电初始化就绪"));

            // Initialize Tab 2 Strategy report
            EvaluateStrategy();

            StatusBanner = "所有模式均由真实 C# 接口与对象图在内存中运行：可直接演练 工厂/装饰器/责任链/状态机/策略/命令撤销/观察者。";
        }

        public IReadOnlyList<PlcVendorOption> VendorOptions { get; }
        public IReadOnlyList<StrategyOption> StrategyOptions { get; }
        public IReadOnlyList<PatternCategoryOption> CategoryFilters { get; }

        public ObservableCollection<PatternTraceEntry> PipelineTrace { get; } = [];
        public ObservableCollection<string> CommandHistory { get; } = [];
        public ObservableCollection<DesignPatternInfo> FilteredPatterns { get; } = [];

        [ObservableProperty]
        public partial int SelectedTabIndex { get; set; }

        [ObservableProperty]
        public partial string StatusBanner { get; set; } = string.Empty;

        // Tab 1: Factory + Decorator + Chain of Responsibility
        [ObservableProperty]
        public partial PlcVendorOption SelectedVendor { get; set; }

        [ObservableProperty]
        public partial bool EnableLoggingDecorator { get; set; } = true;

        [ObservableProperty]
        public partial bool EnableRetryDecorator { get; set; } = true;

        [ObservableProperty]
        public partial bool EnableCacheFallbackDecorator { get; set; }

        [ObservableProperty]
        public partial int SimulateTransientFailures { get; set; } = 2;

        [ObservableProperty]
        public partial bool InjectDuplicateBarcode { get; set; }

        [ObservableProperty]
        public partial bool InjectDimensionFault { get; set; }

        [ObservableProperty]
        public partial bool InjectScratchFault { get; set; }

        [ObservableProperty]
        public partial bool InjectMesOffline { get; set; }

        [ObservableProperty]
        public partial PipelineExecutionResult? PipelineResult { get; set; }

        public string TransientFailuresText => $"{SimulateTransientFailures} 次瞬断";
        public string ActiveDriverName => PipelineResult?.DriverName ?? "—";
        public string DecoratorCountText => $"{PipelineResult?.DecoratorCount ?? 0} 层装饰器";
        public string PlcAttemptsLatencyText => PipelineResult is { } r
            ? string.Create(CultureInfo.InvariantCulture, $"{r.PlcOutcome.Attempts} 次 / {r.PlcOutcome.ElapsedMs:F1} ms")
            : "—";
        public string OverallVerdictBadge => PipelineResult is { } r
            ? (r.OverallPassed ? "PASS 放行" : "NG 拦截")
            : "—";
        public bool IsOverallPassed => PipelineResult?.OverallPassed ?? true;

        // Tab 2: State + Observer + Strategy + Command (Undo/Redo)
        [ObservableProperty]
        public partial EquipmentStateKind CurrentEquipmentState { get; set; }

        [ObservableProperty]
        public partial string CurrentStateDisplayName { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string CurrentStateAllowedHint { get; set; } = string.Empty;

        [ObservableProperty]
        public partial StateTransitionResult? LastTransition { get; set; }

        [ObservableProperty]
        public partial IReadOnlyList<ObserverDispatchRecord> ObserverDispatches { get; set; } = [];

        [ObservableProperty]
        public partial bool SubUiDashboard { get; set; } = true;

        [ObservableProperty]
        public partial bool SubTowerLight { get; set; } = true;

        [ObservableProperty]
        public partial bool SubAuditDb { get; set; } = true;

        [ObservableProperty]
        public partial bool SubMesAlarm { get; set; } = true;

        [ObservableProperty]
        public partial StrategyOption SelectedStrategy { get; set; }

        [ObservableProperty]
        public partial StrategyBatchReport? StrategyReport { get; set; }

        public string RecipeTemperatureText => string.Create(CultureInfo.InvariantCulture, $"{_cmdManager.Target.TemperatureC:F0} °C");
        public string RecipeSpeedText => $"{_cmdManager.Target.ConveyorSpeedMmS} mm/s";
        public string RecipeRecheckText => _cmdManager.Target.AutoRecheckEnabled ? "已开启" : "已关闭";
        public string UndoStackSummary => $"Undo 栈: {_cmdManager.UndoCount}  ·  Redo 栈: {_cmdManager.RedoCount}";
        public bool CanUndoRecipe => _cmdManager.CanUndo;
        public bool CanRedoRecipe => _cmdManager.CanRedo;

        // Tab 3: Pattern Catalog
        [ObservableProperty]
        public partial PatternCategoryOption SelectedCategory { get; set; }

        [ObservableProperty]
        public partial DesignPatternInfo? SelectedPattern { get; set; }

        public bool HasInteractiveLab => SelectedPattern?.InteractiveTabIndex is not null;
        public string JumpToLabButtonText => SelectedPattern?.InteractiveTabIndex switch
        {
            0 => "跳转到 Tab 1 实操演练 →",
            1 => "跳转到 Tab 2 实操演练 →",
            _ => "已在工程核心服务中落地",
        };

        partial void OnSimulateTransientFailuresChanged(int value) => OnPropertyChanged(nameof(TransientFailuresText));

        partial void OnPipelineResultChanged(PipelineExecutionResult? value)
        {
            OnPropertyChanged(nameof(ActiveDriverName));
            OnPropertyChanged(nameof(DecoratorCountText));
            OnPropertyChanged(nameof(PlcAttemptsLatencyText));
            OnPropertyChanged(nameof(OverallVerdictBadge));
            OnPropertyChanged(nameof(IsOverallPassed));
        }

        partial void OnSubUiDashboardChanged(bool value) => SyncObservers();
        partial void OnSubTowerLightChanged(bool value) => SyncObservers();
        partial void OnSubAuditDbChanged(bool value) => SyncObservers();
        partial void OnSubMesAlarmChanged(bool value) => SyncObservers();

        partial void OnSelectedStrategyChanged(StrategyOption value) => EvaluateStrategy();

        partial void OnSelectedCategoryChanged(PatternCategoryOption value)
        {
            FilteredPatterns.Clear();
            var items = value.Category == PatternCategory.All
                ? DesignPatternCatalog.All
                : DesignPatternCatalog.All.Where(p => p.Category == value.Category);

            foreach (var p in items)
            {
                FilteredPatterns.Add(p);
            }

            SelectedPattern = FilteredPatterns.FirstOrDefault();
        }

        partial void OnSelectedPatternChanged(DesignPatternInfo? value)
        {
            OnPropertyChanged(nameof(HasInteractiveLab));
            OnPropertyChanged(nameof(JumpToLabButtonText));
        }

        [RelayCommand]
        private void RunPipeline()
        {
            var res = StationPipelineRunner.Execute(
                SelectedVendor.Vendor,
                EnableLoggingDecorator,
                EnableRetryDecorator,
                EnableCacheFallbackDecorator,
                SimulateTransientFailures,
                InjectDuplicateBarcode,
                InjectDimensionFault,
                InjectScratchFault,
                InjectMesOffline,
                _workpieceSeq++);

            PipelineResult = res;
            PipelineTrace.Clear();
            foreach (var entry in res.Trace)
            {
                PipelineTrace.Add(entry);
            }
        }

        [RelayCommand]
        private void ApplyPipelinePreset(string? preset)
        {
            InjectDuplicateBarcode = false;
            InjectDimensionFault = false;
            InjectScratchFault = false;
            InjectMesOffline = false;

            switch (preset)
            {
                case "Normal":
                    EnableLoggingDecorator = true;
                    EnableRetryDecorator = true;
                    EnableCacheFallbackDecorator = false;
                    SimulateTransientFailures = 0;
                    break;
                case "TransientRetry":
                    EnableLoggingDecorator = true;
                    EnableRetryDecorator = true;
                    EnableCacheFallbackDecorator = false;
                    SimulateTransientFailures = 2;
                    break;
                case "CacheDegrade":
                    EnableLoggingDecorator = true;
                    EnableRetryDecorator = false;
                    EnableCacheFallbackDecorator = true;
                    SimulateTransientFailures = 2;
                    break;
                case "DimensionNg":
                    EnableLoggingDecorator = true;
                    EnableRetryDecorator = true;
                    EnableCacheFallbackDecorator = false;
                    SimulateTransientFailures = 0;
                    InjectDimensionFault = true;
                    break;
                case "MesOffline":
                    EnableLoggingDecorator = true;
                    EnableRetryDecorator = true;
                    EnableCacheFallbackDecorator = false;
                    SimulateTransientFailures = 0;
                    InjectMesOffline = true;
                    break;
            }

            RunPipeline();
        }

        [RelayCommand]
        private void ClearPipelineTrace() => PipelineTrace.Clear();

        [RelayCommand]
        private void FireEquipmentEvent(EquipmentEventKind evt)
        {
            var result = _fsm.Fire(evt);
            LastTransition = result;
            SyncFsmState();
            ObserverDispatches = _publisher.Publish(result);
        }

        [RelayCommand]
        private void EvaluateStrategy()
        {
            if (SelectedStrategy is null) return;
            var strategy = InspectionStrategyFactory.Create(SelectedStrategy.Kind);
            StrategyReport = strategy.EvaluateBatch(_sampleBatch);
        }

        [RelayCommand]
        private void AdjustTempUp() => ExecuteRecipeCmd(new AdjustTemperatureCommand(10));

        [RelayCommand]
        private void AdjustTempDown() => ExecuteRecipeCmd(new AdjustTemperatureCommand(-10));

        [RelayCommand]
        private void AdjustSpeedUp() => ExecuteRecipeCmd(new AdjustSpeedCommand(50));

        [RelayCommand]
        private void ToggleRecheck() => ExecuteRecipeCmd(new ToggleRecheckCommand());

        [RelayCommand(CanExecute = nameof(CanUndoRecipe))]
        private void UndoRecipe()
        {
            var entry = _cmdManager.Undo();
            if (entry is not null)
            {
                PrependCommandHistory(entry);
                RefreshRecipeState();
            }
        }

        [RelayCommand(CanExecute = nameof(CanRedoRecipe))]
        private void RedoRecipe()
        {
            var entry = _cmdManager.Redo();
            if (entry is not null)
            {
                PrependCommandHistory(entry);
                RefreshRecipeState();
            }
        }

        [RelayCommand]
        private void JumpToPatternLab()
        {
            if (SelectedPattern?.InteractiveTabIndex is int tabIdx)
            {
                SelectedTabIndex = tabIdx;
            }
        }

        private void ExecuteRecipeCmd(IRecipeCommand cmd)
        {
            var entry = _cmdManager.Execute(cmd);
            PrependCommandHistory(entry);
            RefreshRecipeState();
        }

        private void PrependCommandHistory(string text)
        {
            CommandHistory.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {text}");
            while (CommandHistory.Count > 24)
            {
                CommandHistory.RemoveAt(CommandHistory.Count - 1);
            }
        }

        private void RefreshRecipeState()
        {
            OnPropertyChanged(nameof(RecipeTemperatureText));
            OnPropertyChanged(nameof(RecipeSpeedText));
            OnPropertyChanged(nameof(RecipeRecheckText));
            OnPropertyChanged(nameof(UndoStackSummary));
            OnPropertyChanged(nameof(CanUndoRecipe));
            OnPropertyChanged(nameof(CanRedoRecipe));
            UndoRecipeCommand.NotifyCanExecuteChanged();
            RedoRecipeCommand.NotifyCanExecuteChanged();
        }

        private void SyncFsmState()
        {
            CurrentEquipmentState = _fsm.Current.Kind;
            CurrentStateDisplayName = _fsm.Current.DisplayName;
            CurrentStateAllowedHint = _fsm.Current.AllowedHint;
        }

        private void SyncObservers()
        {
            SyncSingle(_uiObserver, SubUiDashboard);
            SyncSingle(_towerLightObserver, SubTowerLight);
            SyncSingle(_auditDbObserver, SubAuditDb);
            SyncSingle(_mesObserver, SubMesAlarm);

            if (LastTransition is { } tr)
            {
                ObserverDispatches = _publisher.Publish(tr);
            }
        }

        private void SyncSingle(IEquipmentObserver obs, bool subscribed)
        {
            if (subscribed) _publisher.Subscribe(obs);
            else _publisher.Unsubscribe(obs);
        }
    }
}
