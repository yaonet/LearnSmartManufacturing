namespace LearnSmartManufacturing.Models.Prism
{
    public sealed record PrismConceptInfo(
        string Id,
        string Title,
        string ApiSignature,
        string CategoryBadge,
        string CoreValue,
        string CodeSkeleton,
        string InterviewPitfall,
        int? InteractiveTabIndex);

    public sealed record PrismVsToolkitRow(
        string Dimension,
        string PrismApproach,
        string ToolkitApproach);

    public static class PrismReferenceCatalog
    {
        public static IReadOnlyList<PrismConceptInfo> Concepts { get; } =
        [
            new("bootstrap", "PrismApplication & 容器注册", "PrismApplication · IContainerRegistry (DryIoc)",
                "启动与 DI",
                "接管 App.xaml.cs 生命周期，通过 CreateShell() 创建主窗体，RegisterTypes() 注册单例服务、导航页面与弹窗，屏蔽底层容器差异。",
                """
                public partial class App : PrismApplication
                {
                    protected override Window CreateShell() => Container.Resolve<MainWindow>();
                    protected override void RegisterTypes(IContainerRegistry c)
                    {
                        c.RegisterSingleton<IPlcClient, S7PlcClient>();
                        c.RegisterForNavigation<RecipeEditorView, RecipeEditorViewModel>();
                        c.RegisterDialog<RecipeApproveDialog, RecipeApproveDialogViewModel>();
                    }
                }
                """,
                "面试高频：Prism 8+ 默认推荐使用 DryIoc（性能远超老旧 Unity），同时保留 RegisterForNavigation / RegisterDialog 等高层抽象。",
                null),

            new("locator", "ViewModelLocator 自动装配", "prism:ViewModelLocator.AutoWireViewModel=\"True\"",
                "视图绑定",
                "按命名约定自动将 Views.RecipeEditorView 匹配到 ViewModels.RecipeEditorViewModel，并通过 DI 容器注入构造函数依赖后赋给 DataContext。",
                """
                <UserControl x:Class="App.Views.RecipeEditorView"
                             xmlns:prism="http://prismlibrary.com/"
                             prism:ViewModelLocator.AutoWireViewModel="True">
                    <!-- 零 Code-Behind：自动解析 RecipeEditorViewModel 及其构造函数注入 -->
                </UserControl>
                """,
                "常见踩坑：若目录不叫 Views/ViewModels 或类名未以 View/ViewModel 结尾，AutoWire 会静默找不到！可在 RegisterForNavigation<V, VM>() 显式配对避免反射约定失效。",
                null),

            new("region", "IRegionManager 与多区域 Shell", "prism:RegionManager.RegionName · RequestNavigate",
                "区域导航",
                "把复杂上位机主界面切分为顶部状态栏、左侧导航、主工作区、底部报警条等独立 Region，各业务模块互不引用即可把自己的 View 注入对应区域。",
                """
                <!-- Shell 中声明占位区域 -->
                <ContentControl prism:RegionManager.RegionName="ContentRegion" />
                // 任意模块内无耦合跳转并携带强类型参数
                var p = new NavigationParameters { { "stationId", "ST-02" } };
                _regionManager.RequestNavigate("ContentRegion", "RecipeEditorView", p);
                """,
                "面试追问：Prism 内置支持哪些控件作为 Region？ContentControl、ItemsControl、TabControl、Selector（对应内置 RegionAdapter）；自定义容器需继承 RegionAdapterBase<T>。",
                0),

            new("lifecycle", "导航生命周期与 Journal 回退", "IConfirmNavigationRequest · INavigationAware · KeepAlive",
                "生命周期",
                "提供完整且可拦截的页面切换钩子：离开前校验未保存数据 → 暂停轮询 → 决定复用或重建实例 → 接收参数 → 自动释放非驻留页。",
                """
                public class RecipeVm : IConfirmNavigationRequest, IRegionMemberLifetime
                {
                    public bool KeepAlive => false; // 离开即移出 Region，防内存堆积
                    public void ConfirmNavigationRequest(NavigationContext ctx, Action<bool> cb)
                        => cb(!IsDirty || AskUserConfirm());
                    public void OnNavigatedTo(NavigationContext ctx)
                        => StationId = ctx.Parameters.GetValue<string>("stationId");
                    public bool IsNavigationTarget(NavigationContext ctx) => true;
                }
                """,
                "上位机内存泄漏头号元凶：Prism Region 默认 KeepAlive = true！若高内存视觉图像页不设 KeepAlive = false 且 IsNavigationTarget 返回 false，反复切换会让 Region 越积越多实例。",
                0),

            new("modularity", "IModule 插件化与按需加载", "IModule · [ModuleDependency] · InitializationMode.OnDemand",
                "模块化",
                "大型产线软件按工站/选配功能拆成独立类库 DLL（如视觉/运动/MES 模块），通过 DirectoryModuleCatalog 动态扫描插件，按授权 OnDemand 懒加载。",
                """
                [Module(ModuleName = "MesReportModule", OnDemand = true)]
                [ModuleDependency("PlcCommModule")]
                public class MesReportModule : IModule
                {
                    public void RegisterTypes(IContainerRegistry c) => c.RegisterForNavigation<MesView>();
                    public void OnInitialized(IContainerProvider p) { /* 注入区域或启动后台服务 */ }
                }
                """,
                "面试亮点：通过 [ModuleDependency] 声明拓扑依赖后，调用 _moduleManager.LoadModule(\"MesReportModule\") 会自动先拉起并初始化 PlcCommModule！",
                1),

            new("eventagg", "EventAggregator 跨模块事件总线", "IEventAggregator · PubSubEvent<T> · ThreadOption.UIThread",
                "松耦合通信",
                "替代 C# 原生 event 的跨模块发布-订阅枢纽：内置线程调度（自动切回 UI 线程）、消息过滤器 Filter 与默认弱引用防内存泄漏。",
                """
                public sealed class StationAlarmEvent : PubSubEvent<AlarmPayload> { }
                // 订阅：默认 keepSubscriberReferenceAlive = false（弱引用！）
                _ea.GetEvent<StationAlarmEvent>().Subscribe(
                    OnAlarm, ThreadOption.UIThread, keepSubscriberReferenceAlive: false,
                    filter: a => a.Severity == AlarmSeverity.Critical);
                // 发布（可在 PLC 后台接收线程直接调用）
                _ea.GetEvent<StationAlarmEvent>().Publish(new AlarmPayload(...));
                """,
                "经典面试坑：① Subscribe 传 Lambda 闭包且使用默认弱引用时，匿名委托可能被 GC 提前回收导致收不到消息！② 设 keepSubscriberReferenceAlive: true 时务必保存 SubscriptionToken 并在关闭时 Dispose/Unsubscribe。",
                1),

            new("dialog_cmd", "IDialogService 与 CompositeCommand", "IDialogService · IDialogAware · CompositeCommand",
                "交互与命令",
                "IDialogService 彻底消灭 ViewModel 里直接 new Window().ShowDialog() 的测试噩梦；CompositeCommand 将多个子模块的保存/急停命令聚合成一个全局工具栏按钮。",
                """
                // 1. MVVM 可测弹窗
                _dialogService.ShowDialog("RecipeApproveDialog", p, r =>
                {
                    if (r.Result == ButtonResult.OK) ApplyRecipe(r.Parameters);
                });
                // 2. 全局聚合命令（任一子命令 CanExecute=false 则整体禁用）
                _saveAllCommand.RegisterCommand(plcModule.SaveCommand);
                _saveAllCommand.RegisterCommand(visionModule.SaveCommand);
                """,
                "IDialogAware 的 CanCloseDialog() 非常适合工业防呆：例如操作员权限密码未校验通过时禁止点击确定关闭弹窗。",
                1),

            new("compare", "Prism 9 商业授权与 Toolkit 选型", "Prism 8.1 (MIT) / Prism 9 (Commercial) vs CommunityToolkit.Mvvm",
                "架构选型",
                "面试官极爱问“为什么你们项目选 Prism 还是 CommunityToolkit.Mvvm？”——能讲清团队规模、模块化诉求、源生成器性能与授权成本直接拉满架构分。",
                """
                // 本项目采用的现代轻量组合（MIT 免费 + .NET 源生成器零反射）：
                // CommunityToolkit.Mvvm ([ObservableProperty] / [RelayCommand] / WeakReferenceMessenger)
                // + Microsoft.Extensions.Hosting (标准 DI + IServiceScope 页面级资源释放)
                // 若为 10+ 人多团队插件化大型 SCADA/MES 平台 → 优先考虑 Prism (Region + ModuleCatalog)
                """,
                "重要行业常识：Prism 9.0 起引入了商业授权条款（年营收超 $1M 团队需购买商业许可），而 Prism 8.1.97 为最后一个纯 MIT 版本；新立项中小型上位机更推荐 CommunityToolkit.Mvvm + Microsoft.Extensions.DependencyInjection。",
                null),
        ];

        public static IReadOnlyList<PrismVsToolkitRow> ComparisonRows { get; } =
        [
            new("核心定位", "企业级全栈复合应用框架 (Region/Module/Dialog/DI)", "轻量高性能 MVVM 工具包 (仅解决通知/命令/消息)"),
            new("代码简洁度", "BindableBase + SetProperty (手写属性或 Fody)", "[ObservableProperty] / [RelayCommand] 编译期源生成"),
            new("多模块/插件化", "原生 IModuleCatalog + DirectoryCatalog 动态加载 DLL", "需自行结合 Microsoft.DI / AssemblyLoadContext 实现"),
            new("区域导航/拦截", "原生 IRegionManager + IConfirmNavigationRequest + Journal", "需自行封装 INavigationService（如本项目）"),
            new("跨模块通信", "IEventAggregator (内置 UIThread 调度与 Filter)", "WeakReferenceMessenger (强类型高性能弱引用消息)"),
            new("开源与商用授权", "Prism 8.1 为 MIT；Prism 9.0+ 为双轨商业授权", "微软官方维护 · 纯 MIT 永久免费商用"),
        ];
    }
}
