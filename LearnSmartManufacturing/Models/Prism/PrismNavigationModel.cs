namespace LearnSmartManufacturing.Models.Prism
{
    public sealed record PrismNavigationParameters(string StationId, string RecipeId)
    {
        public string Format() => $"?stationId={StationId}&recipeId={RecipeId}";
    }

    public sealed record PrismRegionViewInstance(
        string ViewKey,
        string DisplayName,
        int InstanceId,
        bool KeepAlive,
        bool IsActive,
        string LastParamsText);

    public sealed record PrismLifecycleStep(
        string HookName,
        string Target,
        string Detail,
        string Status); // "Pass" | "Warn" | "Blocked" | "Info"

    public sealed record PrismNavigationOutcome(
        bool Succeeded,
        string ActiveViewKey,
        string ActiveViewTitle,
        int ActiveInstanceId,
        string Summary,
        IReadOnlyList<PrismLifecycleStep> Steps,
        IReadOnlyList<PrismRegionViewInstance> RegionViews,
        IReadOnlyList<string> JournalEntries,
        int JournalIndex);

    /// <summary>
    /// Authentic in-memory simulation of Prism's IRegionManager, INavigationAware,
    /// IConfirmNavigationRequest, IRegionMemberLifetime, and IRegionNavigationJournal.
    /// </summary>
    public sealed class SimulatedPrismRegionManager
    {
        private sealed class HostedView
        {
            public required string ViewKey { get; init; }
            public required string DisplayName { get; init; }
            public required int InstanceId { get; init; }
            public bool KeepAlive { get; set; } = true;
            public string LastParamsText { get; set; } = string.Empty;
        }

        private readonly Dictionary<string, string> _viewTitles = new()
        {
            ["StationOverviewView"] = "工站总览 (ViewA)",
            ["RecipeEditorView"] = "工艺配方 (ViewB)",
            ["AlarmDiagnosticsView"] = "报警诊断 (ViewC)",
        };

        private readonly List<HostedView> _regionViews = [];
        private readonly List<(string ViewKey, PrismNavigationParameters Params)> _journal = [];
        private int _journalIndex = -1;
        private int _nextInstanceId = 1;
        private HostedView? _active;

        public SimulatedPrismRegionManager()
        {
            var initialParams = new PrismNavigationParameters("ST-01", "RCP-A09");
            var first = new HostedView
            {
                ViewKey = "StationOverviewView",
                DisplayName = _viewTitles["StationOverviewView"],
                InstanceId = _nextInstanceId++,
                KeepAlive = true,
                LastParamsText = initialParams.Format(),
            };
            _regionViews.Add(first);
            _active = first;
            _journal.Add((first.ViewKey, initialParams));
            _journalIndex = 0;
        }

        public bool CanGoBack => _journalIndex > 0;
        public bool CanGoForward => _journalIndex >= 0 && _journalIndex < _journal.Count - 1;
        public string ActiveViewKey => _active?.ViewKey ?? string.Empty;
        public int RegionViewCount => _regionViews.Count;

        public PrismNavigationOutcome RequestNavigate(
            string targetViewKey,
            PrismNavigationParameters parameters,
            bool isCurrentDirty,
            bool confirmAllowsLeave,
            bool reuseExistingTarget,
            bool currentKeepAlive,
            bool recordJournal = true)
        {
            var steps = new List<PrismLifecycleStep>();
            if (!_viewTitles.TryGetValue(targetViewKey, out var targetTitle))
            {
                steps.Add(new("RequestNavigate", targetViewKey, "未在 IContainerRegistry 注册此导航视图", "Blocked"));
                return BuildOutcome(false, $"导航失败：未注册视图 {targetViewKey}", steps);
            }

            var current = _active;
            if (current is not null)
            {
                current.KeepAlive = currentKeepAlive;

                // 1. IConfirmNavigationRequest
                if (isCurrentDirty && !confirmAllowsLeave)
                {
                    steps.Add(new("ConfirmNavigationRequest", $"{current.ViewKey}#{current.InstanceId}",
                        "当前页面存在未保存配方，回调 continuationCallback(false) 拦截导航！", "Blocked"));
                    return BuildOutcome(false, $"已拦截：{current.DisplayName} 存在未保存修改，用户取消离开", steps);
                }

                steps.Add(new("ConfirmNavigationRequest", $"{current.ViewKey}#{current.InstanceId}",
                    isCurrentDirty ? "存在未保存修改，但用户确认放行 continuationCallback(true)" : "无阻塞条件，放行 continuationCallback(true)",
                    isCurrentDirty ? "Warn" : "Pass"));

                // 2. INavigationAware.OnNavigatedFrom
                steps.Add(new("OnNavigatedFrom", $"{current.ViewKey}#{current.InstanceId}",
                    "暂停当前页高频轮询与实时订阅，保存局部状态", "Pass"));
            }

            // 3. INavigationAware.IsNavigationTarget
            var existing = _regionViews.FirstOrDefault(v => v.ViewKey == targetViewKey);
            HostedView target;
            if (existing is not null && reuseExistingTarget)
            {
                target = existing;
                steps.Add(new("IsNavigationTarget", $"{target.ViewKey}#{target.InstanceId}",
                    "返回 true → 复用 Region 已有实例（保留历史筛选/滚屏状态）", "Pass"));
            }
            else
            {
                target = new HostedView
                {
                    ViewKey = targetViewKey,
                    DisplayName = targetTitle,
                    InstanceId = _nextInstanceId++,
                    KeepAlive = true,
                };
                if (existing is not null)
                {
                    _regionViews.Remove(existing);
                }

                _regionViews.Add(target);
                steps.Add(new("IsNavigationTarget", $"{target.ViewKey}#{target.InstanceId}",
                    existing is null
                        ? $"首次进入 → 容器解析新实例 #{target.InstanceId}"
                        : $"返回 false → 销毁旧实例并创建全新实例 #{target.InstanceId}",
                    "Warn"));
            }

            // 4. INavigationAware.OnNavigatedTo
            target.LastParamsText = parameters.Format();
            _active = target;
            steps.Add(new("OnNavigatedTo", $"{target.ViewKey}#{target.InstanceId}",
                $"接收 NavigationParameters: {parameters.Format()}", "Pass"));

            // 5. IRegionMemberLifetime.KeepAlive
            if (current is not null && current != target)
            {
                if (!current.KeepAlive)
                {
                    _regionViews.Remove(current);
                    steps.Add(new("IRegionMemberLifetime", $"{current.ViewKey}#{current.InstanceId}",
                        "KeepAlive = false → 已自动从 ContentRegion 移除释放，避免常驻内存泄漏", "Warn"));
                }
                else
                {
                    steps.Add(new("IRegionMemberLifetime", $"{current.ViewKey}#{current.InstanceId}",
                        "KeepAlive = true → 保留在 ContentRegion 缓存池等待下次秒切", "Info"));
                }
            }

            if (recordJournal)
            {
                if (_journalIndex < _journal.Count - 1)
                {
                    _journal.RemoveRange(_journalIndex + 1, _journal.Count - _journalIndex - 1);
                }

                _journal.Add((targetViewKey, parameters));
                _journalIndex = _journal.Count - 1;
            }

            return BuildOutcome(true, $"导航成功 → {target.DisplayName} (#{target.InstanceId}) {parameters.Format()}", steps);
        }

        public PrismNavigationOutcome GoBack(bool isCurrentDirty, bool confirmAllowsLeave, bool reuseExistingTarget, bool currentKeepAlive)
        {
            if (!CanGoBack)
            {
                return BuildOutcome(false, "Journal 已位于最早历史记录，无法继续后退",
                    [new("IRegionNavigationJournal", "GoBack()", "回退栈已到顶", "Warn")]);
            }

            var targetIdx = _journalIndex - 1;
            var entry = _journal[targetIdx];
            var res = RequestNavigate(entry.ViewKey, entry.Params, isCurrentDirty, confirmAllowsLeave, reuseExistingTarget, currentKeepAlive, recordJournal: false);
            if (res.Succeeded)
            {
                _journalIndex = targetIdx;
                return BuildOutcome(true, $"Journal GoBack → {res.ActiveViewTitle} (#{res.ActiveInstanceId})", res.Steps);
            }

            return res;
        }

        public PrismNavigationOutcome GoForward(bool isCurrentDirty, bool confirmAllowsLeave, bool reuseExistingTarget, bool currentKeepAlive)
        {
            if (!CanGoForward)
            {
                return BuildOutcome(false, "Journal 已位于最新历史记录，无法前进",
                    [new("IRegionNavigationJournal", "GoForward()", "前进栈为空", "Warn")]);
            }

            var targetIdx = _journalIndex + 1;
            var entry = _journal[targetIdx];
            var res = RequestNavigate(entry.ViewKey, entry.Params, isCurrentDirty, confirmAllowsLeave, reuseExistingTarget, currentKeepAlive, recordJournal: false);
            if (res.Succeeded)
            {
                _journalIndex = targetIdx;
                return BuildOutcome(true, $"Journal GoForward → {res.ActiveViewTitle} (#{res.ActiveInstanceId})", res.Steps);
            }

            return res;
        }

        public PrismNavigationOutcome GetSnapshot() =>
            BuildOutcome(true, $"ContentRegion 已就绪 · 当前视图 {_active?.DisplayName} (#{_active?.InstanceId})",
                [new("OnInitialized", "ContentRegion", $"注册初始视图 {_active?.ViewKey}#{_active?.InstanceId}", "Pass")]);

        private PrismNavigationOutcome BuildOutcome(bool succeeded, string summary, IReadOnlyList<PrismLifecycleStep> steps)
        {
            var views = _regionViews.Select(v => new PrismRegionViewInstance(
                v.ViewKey, v.DisplayName, v.InstanceId, v.KeepAlive, v == _active, v.LastParamsText)).ToList();

            var journalTexts = _journal.Select((j, i) =>
                $"{(i == _journalIndex ? "▶ " : "")}{_viewTitles[j.ViewKey]} ({j.Params.StationId})").ToList();

            return new PrismNavigationOutcome(
                succeeded,
                _active?.ViewKey ?? string.Empty,
                _active?.DisplayName ?? string.Empty,
                _active?.InstanceId ?? 0,
                summary,
                steps,
                views,
                journalTexts,
                _journalIndex);
        }
    }
}
