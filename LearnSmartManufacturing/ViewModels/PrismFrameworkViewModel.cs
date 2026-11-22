using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LearnSmartManufacturing.Models.Prism;

namespace LearnSmartManufacturing.ViewModels
{
    public sealed record PrismViewOption(string ViewKey, string DisplayName);
    public sealed record PrismSeverityOption(PrismAlarmSeverity Severity, string Name);
    public sealed record PrismThreadChoice(PrismThreadOption Option, string Name);

    public partial class PrismFrameworkViewModel : ObservableObject
    {
        private readonly SimulatedPrismRegionManager _regionManager = new();
        private readonly SimulatedModuleCatalog _moduleCatalog = new();

        public PrismFrameworkViewModel()
        {
            TargetViewOptions =
            [
                new("StationOverviewView", "工站总览 (StationOverviewView)"),
                new("RecipeEditorView", "工艺配方 (RecipeEditorView)"),
                new("AlarmDiagnosticsView", "报警诊断 (AlarmDiagnosticsView)"),
            ];

            AlarmSeverityOptions =
            [
                new(PrismAlarmSeverity.Info, "Info (普通节拍事件)"),
                new(PrismAlarmSeverity.Warning, "Warning (参数预警)"),
                new(PrismAlarmSeverity.Critical, "Critical (急停/严重故障)"),
            ];

            ThreadOptions =
            [
                new(PrismThreadOption.UIThread, "ThreadOption.UIThread (自动切主线程)"),
                new(PrismThreadOption.BackgroundThread, "ThreadOption.BackgroundThread (后台线程池)"),
                new(PrismThreadOption.PublisherThread, "ThreadOption.PublisherThread (沿用发布线程)"),
            ];

            Concepts = PrismReferenceCatalog.Concepts;
            ComparisonRows = PrismReferenceCatalog.ComparisonRows;

            SelectedTargetView = TargetViewOptions[1]; // RecipeEditorView
            SelectedAlarmSeverity = AlarmSeverityOptions[2]; // Critical
            SelectedThreadOption = ThreadOptions[0]; // UIThread
            SelectedConcept = Concepts[2]; // IRegionManager

            // Initial Tab 1 state: perform one navigation from ViewA -> ViewB so both views & full 5-hook chain show immediately
            ApplyNavOutcome(_regionManager.RequestNavigate(
                "RecipeEditorView",
                new PrismNavigationParameters(ParamStationId, ParamRecipeId),
                isCurrentDirty: false,
                confirmAllowsLeave: true,
                reuseExistingTarget: true,
                currentKeepAlive: true));

            // Initial Tab 2 state: Modules + EventAggregator + CompositeCommand + DialogService
            RefreshModules();
            PublishAlarm();
            RefreshCompositeCommand(executeNow: false);
            DialogStatusBanner = "尚未打开弹窗 · 点击“ShowDialog(...)”演示 IDialogAware 生命周期与 CanCloseDialog() 拦截。";

            StatusBanner = "零外部依赖内存仿真 Prism 核心机制：IRegionManager / INavigationAware / IModuleCatalog / EventAggregator / CompositeCommand / IDialogService。";
        }

        public IReadOnlyList<PrismViewOption> TargetViewOptions { get; }
        public IReadOnlyList<PrismSeverityOption> AlarmSeverityOptions { get; }
        public IReadOnlyList<PrismThreadChoice> ThreadOptions { get; }
        public IReadOnlyList<PrismConceptInfo> Concepts { get; }
        public IReadOnlyList<PrismVsToolkitRow> ComparisonRows { get; }

        public ObservableCollection<string> NavigationLog { get; } = [];
        public ObservableCollection<string> ModuleLoadLogs { get; } = [];

        [ObservableProperty]
        public partial int SelectedTabIndex { get; set; }

        [ObservableProperty]
        public partial string StatusBanner { get; set; } = string.Empty;

        // Tab 1: Region Navigation & Lifecycle
        [ObservableProperty]
        public partial PrismViewOption SelectedTargetView { get; set; }

        [ObservableProperty]
        public partial string ParamStationId { get; set; } = "ST-02";

        [ObservableProperty]
        public partial string ParamRecipeId { get; set; } = "RCP-B18";

        [ObservableProperty]
        public partial bool IsCurrentRecipeDirty { get; set; }

        [ObservableProperty]
        public partial bool ConfirmDialogAllowsLeave { get; set; }

        [ObservableProperty]
        public partial bool ReuseNavigationTarget { get; set; } = true;

        [ObservableProperty]
        public partial bool KeepAliveInRegion { get; set; } = true;

        [ObservableProperty]
        public partial PrismNavigationOutcome? NavigationOutcome { get; set; }

        public string ActiveViewSummary => NavigationOutcome is { } r ? $"{r.ActiveViewTitle} (#{r.ActiveInstanceId})" : "—";
        public string RegionCountText => $"{NavigationOutcome?.RegionViews.Count ?? 0} 个视图实例";
        public string JournalDepthText => NavigationOutcome is { } r ? $"{r.JournalIndex + 1} / {r.JournalEntries.Count}" : "0 / 0";
        public string LastNavStatusText => NavigationOutcome?.Succeeded == true ? "OK 放行" : "Canceled 拦截";
        public bool IsLastNavSucceeded => NavigationOutcome?.Succeeded ?? true;
        public bool CanGoBack => _regionManager.CanGoBack;
        public bool CanGoForward => _regionManager.CanGoForward;

        // Tab 2: Modularity, EventAggregator, CompositeCommand, IDialogService
        [ObservableProperty]
        public partial IReadOnlyList<PrismModuleSnapshot> ModuleSnapshots { get; set; } = [];

        [ObservableProperty]
        public partial PrismSeverityOption SelectedAlarmSeverity { get; set; }

        [ObservableProperty]
        public partial PrismThreadChoice SelectedThreadOption { get; set; }

        [ObservableProperty]
        public partial bool FilterCriticalOnly { get; set; }

        [ObservableProperty]
        public partial bool KeepStrongReference { get; set; }

        [ObservableProperty]
        public partial IReadOnlyList<PrismEventDeliveryRecord> EventDeliveries { get; set; } = [];

        [ObservableProperty]
        public partial bool PlcModuleCanSave { get; set; } = true;

        [ObservableProperty]
        public partial bool VisionModuleCanSave { get; set; } = true;

        [ObservableProperty]
        public partial bool MotionModuleCanSave { get; set; } = true;

        [ObservableProperty]
        public partial PrismCompositeExecutionResult? CompositeResult { get; set; }

        public bool CanExecuteCompositeSave => CompositeResult?.CanExecuteAll ?? false;

        [ObservableProperty]
        public partial bool IsDialogOpen { get; set; }

        [ObservableProperty]
        public partial bool DialogOperatorVerified { get; set; }

        [ObservableProperty]
        public partial string DialogIncomingParamsText { get; set; } = "recipeId=RCP-B18 · operator=Admin";

        [ObservableProperty]
        public partial string DialogStatusBanner { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsDialogResultOk { get; set; } = true;

        // Tab 3: Reference Catalog
        [ObservableProperty]
        public partial PrismConceptInfo? SelectedConcept { get; set; }

        public bool HasConceptLab => SelectedConcept?.InteractiveTabIndex is not null;
        public string JumpToConceptLabText => SelectedConcept?.InteractiveTabIndex switch
        {
            0 => "跳转到 Tab 1 实操演练 →",
            1 => "跳转到 Tab 2 实操演练 →",
            _ => "框架架构概念",
        };

        partial void OnPlcModuleCanSaveChanged(bool value) => RefreshCompositeCommand(executeNow: false);
        partial void OnVisionModuleCanSaveChanged(bool value) => RefreshCompositeCommand(executeNow: false);
        partial void OnMotionModuleCanSaveChanged(bool value) => RefreshCompositeCommand(executeNow: false);

        partial void OnSelectedConceptChanged(PrismConceptInfo? value)
        {
            OnPropertyChanged(nameof(HasConceptLab));
            OnPropertyChanged(nameof(JumpToConceptLabText));
        }

        [RelayCommand]
        private void Navigate()
        {
            var p = new PrismNavigationParameters(
                string.IsNullOrWhiteSpace(ParamStationId) ? "ST-01" : ParamStationId.Trim(),
                string.IsNullOrWhiteSpace(ParamRecipeId) ? "RCP-001" : ParamRecipeId.Trim());

            var outcome = _regionManager.RequestNavigate(
                SelectedTargetView.ViewKey, p,
                IsCurrentRecipeDirty, ConfirmDialogAllowsLeave,
                ReuseNavigationTarget, KeepAliveInRegion);

            ApplyNavOutcome(outcome);
        }

        [RelayCommand(CanExecute = nameof(CanGoBack))]
        private void GoBack()
        {
            var outcome = _regionManager.GoBack(
                IsCurrentRecipeDirty, ConfirmDialogAllowsLeave,
                ReuseNavigationTarget, KeepAliveInRegion);
            ApplyNavOutcome(outcome);
        }

        [RelayCommand(CanExecute = nameof(CanGoForward))]
        private void GoForward()
        {
            var outcome = _regionManager.GoForward(
                IsCurrentRecipeDirty, ConfirmDialogAllowsLeave,
                ReuseNavigationTarget, KeepAliveInRegion);
            ApplyNavOutcome(outcome);
        }

        [RelayCommand]
        private void ApplyNavPreset(string? preset)
        {
            switch (preset)
            {
                case "DirtyBlock":
                    IsCurrentRecipeDirty = true;
                    ConfirmDialogAllowsLeave = false;
                    SelectedTargetView = TargetViewOptions[2];
                    Navigate();
                    break;
                case "EvictKeepAlive":
                    IsCurrentRecipeDirty = false;
                    KeepAliveInRegion = false;
                    ReuseNavigationTarget = true;
                    SelectedTargetView = TargetViewOptions[2];
                    Navigate();
                    break;
                case "ForceRecreate":
                    IsCurrentRecipeDirty = false;
                    KeepAliveInRegion = true;
                    ReuseNavigationTarget = false;
                    SelectedTargetView = TargetViewOptions[0];
                    Navigate();
                    break;
            }
        }

        [RelayCommand]
        private void ClearNavLog() => NavigationLog.Clear();

        [RelayCommand]
        private void LoadModule(string? moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName)) return;
            var entries = _moduleCatalog.LoadModule(moduleName);
            foreach (var line in entries)
            {
                ModuleLoadLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {line}");
            }

            while (ModuleLoadLogs.Count > 24)
            {
                ModuleLoadLogs.RemoveAt(ModuleLoadLogs.Count - 1);
            }

            RefreshModules();
        }

        [RelayCommand]
        private void ResetModules()
        {
            _moduleCatalog.Reset();
            ModuleLoadLogs.Clear();
            ModuleLoadLogs.Add($"[{DateTime.Now:HH:mm:ss}] [Reset] 模块目录已重置，仅 CoreAuthModule (WhenAvailable) 启动加载");
            RefreshModules();
        }

        [RelayCommand]
        private void PublishAlarm()
        {
            EventDeliveries = SimulatedEventAggregator.PublishAlarm(
                SelectedAlarmSeverity.Severity,
                SelectedThreadOption.Option,
                FilterCriticalOnly,
                KeepStrongReference);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteCompositeSave))]
        private void ExecuteCompositeSave() => RefreshCompositeCommand(executeNow: true);

        [RelayCommand]
        private void OpenApproveDialog()
        {
            IsDialogOpen = true;
            DialogOperatorVerified = false;
            DialogIncomingParamsText = $"stationId={ParamStationId} · recipeId={ParamRecipeId}";
            IsDialogResultOk = true;
            DialogStatusBanner = $"OnDialogOpened({DialogIncomingParamsText}) 已触发 · 等待操作员确认（请试着在未校验密码时直接点“确定 OK”）";
        }

        [RelayCommand]
        private void ConfirmApproveDialog()
        {
            if (!IsDialogOpen) return;
            if (!DialogOperatorVerified)
            {
                IsDialogResultOk = false;
                DialogStatusBanner = "CanCloseDialog() 返回 false：操作员权限密码尚未校验通过，禁止关闭并放行配方！";
                return;
            }

            IsDialogOpen = false;
            IsDialogResultOk = true;
            DialogStatusBanner = $"RequestClose(ButtonResult.OK) → 回调收到 approvedBy=Admin ({DialogIncomingParamsText}) ✓";
        }

        [RelayCommand]
        private void CancelApproveDialog()
        {
            if (!IsDialogOpen) return;
            IsDialogOpen = false;
            IsDialogResultOk = false;
            DialogStatusBanner = "RequestClose(ButtonResult.Cancel) → 用户取消审批，原配方保持不变。";
        }

        [RelayCommand]
        private void JumpToConceptLab()
        {
            if (SelectedConcept?.InteractiveTabIndex is int tabIdx)
            {
                SelectedTabIndex = tabIdx;
            }
        }

        private void ApplyNavOutcome(PrismNavigationOutcome outcome)
        {
            NavigationOutcome = outcome;
            var ts = DateTime.Now.ToString("HH:mm:ss");
            foreach (var step in outcome.Steps.Reverse())
            {
                NavigationLog.Insert(0, $"[{ts}] [{step.HookName}] {step.Target} · {step.Detail}");
            }

            while (NavigationLog.Count > 40)
            {
                NavigationLog.RemoveAt(NavigationLog.Count - 1);
            }

            OnPropertyChanged(nameof(ActiveViewSummary));
            OnPropertyChanged(nameof(RegionCountText));
            OnPropertyChanged(nameof(JournalDepthText));
            OnPropertyChanged(nameof(LastNavStatusText));
            OnPropertyChanged(nameof(IsLastNavSucceeded));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            GoBackCommand.NotifyCanExecuteChanged();
            GoForwardCommand.NotifyCanExecuteChanged();
        }

        private void RefreshModules() => ModuleSnapshots = _moduleCatalog.GetSnapshots();

        private void RefreshCompositeCommand(bool executeNow)
        {
            CompositeResult = SimulatedCompositeCommand.Evaluate(
                PlcModuleCanSave, VisionModuleCanSave, MotionModuleCanSave, executeNow);
            OnPropertyChanged(nameof(CanExecuteCompositeSave));
            ExecuteCompositeSaveCommand.NotifyCanExecuteChanged();
        }
    }
}
