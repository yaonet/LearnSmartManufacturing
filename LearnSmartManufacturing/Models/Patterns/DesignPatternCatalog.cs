namespace LearnSmartManufacturing.Models.Patterns
{
    public enum PatternCategory
    {
        All,
        Creational,
        Structural,
        Behavioral,
    }

    public sealed record DesignPatternInfo(
        string Id,
        string Name,
        string EnglishName,
        PatternCategory Category,
        string CategoryLabel,
        string CoreIntent,
        string IndustrialScenario,
        string ProjectMapping,
        string CodeSkeleton,
        string InterviewPitfall,
        int? InteractiveTabIndex);

    /// <summary>
    /// Curated reference catalog of the 10 most frequently used GoF design patterns in
    /// industrial upper-computer (C# / WPF) software architecture and interviews.
    /// </summary>
    public static class DesignPatternCatalog
    {
        public static IReadOnlyList<DesignPatternInfo> All { get; } =
        [
            new("singleton", "单例模式 & DI 生命周期", "Singleton / DI Container",
                PatternCategory.Creational, "创建型",
                "保证硬件连接池、全局配置或日志服务在进程内仅存在单一受控实例，避免串口/板卡句柄重复占用。",
                "串口 COM 管理中心、全局报警日志写入器、运动控制卡板级句柄管理。",
                "App.xaml.cs 中用 AddSingleton<INavigationService> / AddSingleton<IThemeService> 替代手写静态单例；页面切走时由 IServiceScope 自动释放 Scoped 资源。",
                """
                // 推荐：交给 DI 容器托管生命周期，而非到处写 static Instance
                services.AddSingleton<IThemeService, ThemeService>();
                // 若脱离 DI：使用 Lazy<T> 保证无锁延迟与线程安全
                private static readonly Lazy<SerialHub> _inst = new(() => new SerialHub());
                public static SerialHub Instance => _inst.Value;
                """,
                "面试高频：为什么现代 C# 不建议滥用手写静态单例？① 隐式全局状态导致单元测试难以 Mock；② 生命周期不可控；应优先用 DI 容器的 AddSingleton 注入接口。",
                null),

            new("factory", "工厂方法 / 抽象工厂", "Factory Method / Abstract Factory",
                PatternCategory.Creational, "创建型",
                "将具体硬件驱动或协议实例的 new 过程封装起来，调用方只面向抽象接口编程，新增型号满足开闭原则 (OCP)。",
                "同一套上位机兼容西门子 S7 / 三菱 MC / 欧姆龙 FINS PLC，或切换海康 / Basler / 迈德威视相机 SDK。",
                "本页 Tab 1 的 PlcDriverFactory.Create(vendor) 按品牌返回 IPlcDriver；协议模块中多品牌驱动切换均遵循此思路。",
                """
                public interface IPlcDriver { PlcReadOutcome Read(string addr); }
                public static class PlcDriverFactory
                {
                    public static IPlcDriver Create(PlcVendorKind v) => v switch
                    {
                        PlcVendorKind.SiemensS7    => new SiemensS7Driver(),
                        PlcVendorKind.MitsubishiMc => new MitsubishiMcDriver(),
                        _                          => new OmronFinsDriver(),
                    };
                }
                """,
                "工厂方法 vs 抽象工厂：工厂方法针对单一产品族（如创建某个 PLC 驱动）；抽象工厂针对一整套配套产品族（如某品牌一套“相机 + 光源控制器 + 标定库”）。",
                0),

            new("adapter", "适配器模式", "Adapter",
                PatternCategory.Structural, "结构型",
                "把厂商差异极大的第三方非标 SDK（C++ P/Invoke、DLL 回调）统一翻译为项目内部标准的硬件抽象层 (HAL) 接口。",
                "固高 / 雷赛 / 研华运动控制卡函数签名各异，通过 Adapter 统一适配为内部 IMotionAxis（使能、回零、绝对定位）。",
                "运动控制模块中的 IMotionAxis / IXyMotionStage 即是典型 HAL 适配目标：仿真器与真实板卡驱动通过相同接口无缝替换。",
                """
                // 将厂商原生非标 SDK 适配为统一 IMotionAxis
                public sealed class GoogolAxisAdapter(GoogolNativeSdk sdk, short axis) : IMotionAxis
                {
                    public Task MoveAbsAsync(double mm) =>
                        Task.Run(() => sdk.GT_SetPos(axis, (int)(mm * 1000)));
                }
                """,
                "适配器 vs 装饰器：适配器改变接口形态（把 A 接口转成 B 接口）；装饰器保持接口完全不变，只动态叠加日志/重试/缓存等增强能力。",
                null),

            new("decorator", "装饰器模式", "Decorator",
                PatternCategory.Structural, "结构型",
                "在不修改原驱动代码的前提下，以“洋葱圈”方式动态嵌套叠加日志、重试、超时熔断、缓存降级等横切能力。",
                "在基础 PLC/Modbus 客户端外按需组合 LoggingDecorator + RetryDecorator + CacheFallbackDecorator，替代类爆炸继承。",
                "本页 Tab 1 支持勾选任意装饰器组合，并可注入“前 2 次通讯瞬断”实测 Retry 透明恢复与 CacheFallback 降级效果。",
                """
                IPlcDriver driver = PlcDriverFactory.Create(vendor);
                if (enableRetry)   driver = new RetryDriverDecorator(driver, maxAttempts: 3);
                if (enableCache)   driver = new CacheFallbackDriverDecorator(driver);
                if (enableLogging) driver = new LoggingDriverDecorator(driver);
                var result = driver.ReadStationData("DB1.DBD4");
                """,
                "面试高频：装饰器 vs 继承？若 3 种能力（日志/重试/缓存）用继承排列组合需要 2^3=8 个子类，装饰器只需 3 个包装类且运行期可动态开关！",
                0),

            new("facade", "外观模式", "Facade",
                PatternCategory.Structural, "结构型",
                "为复杂的底层多子系统（PLC + 视觉相机 + 运动卡 + MES）提供简洁的单一高层入口，降低 UI/ViewModel 复杂度。",
                "产线一键“整机回零”或“一键换型”背后需协调气缸复位、光源亮度切换、相机加载配方与 PLC 参数下发，由 StationFacade 统一编排。",
                "本页 Tab 1 的 StationPipelineRunner.Execute(...) 即是外观入口，对外屏蔽了工厂创建、装饰器组装与责任链串联的全部细节。",
                """
                public sealed class StationFacade(IPlcDriver plc, ICamera cam, IMesClient mes)
                {
                    public async Task<bool> RunOneCycleAsync(CancellationToken ct)
                    {
                        var temp = await plc.ReadAsync("DB1.DBD4", ct);
                        var ok   = await cam.GrabAndInspectAsync(ct);
                        return ok && await mes.UploadAsync(temp, ct);
                    }
                }
                """,
                "注意避免把 Facade 写成无所不能的“上帝类 (God Object)”：Facade 只做流程协调，不承接各子系统内部核心算法。",
                null),

            new("strategy", "策略模式", "Strategy",
                PatternCategory.Behavioral, "行为型",
                "把一组可互换的算法封装成独立类并实现统一接口，运行期按配方/订单动态切换，消灭臃肿的 if-else 分支。",
                "不同客户产品切换视觉质检判定策略（车规零缺陷 vs 标准 3σ vs 高节拍抽检），或运动控制切换梯形 T 型 / S 型加减速曲线。",
                "本页 Tab 2 实测 3 种 IInspectionStrategy 对同一 200 件样本批次的良率、漏检率与节拍耗时对比；运动模块亦使用策略思想切换 T/S 规划。",
                """
                public interface IInspectionStrategy
                {
                    StrategyBatchReport EvaluateBatch(ReadOnlySpan<double> defectScores);
                }
                // 运行期按配方热切换，无需改动主采集循环
                IInspectionStrategy strategy = InspectionStrategyFactory.Create(selectedKind);
                var report = strategy.EvaluateBatch(batchScores);
                """,
                "策略 vs 状态模式：类图几乎相同！区别在意图——策略由外部主动指定/切换（且彼此平行）；状态由对象内部根据当前状态与事件自动流转。",
                1),

            new("state", "状态模式", "State",
                PatternCategory.Behavioral, "行为型",
                "把设备的每种运行状态封装为独立状态对象，状态流转与联锁守卫规则内聚在各自状态类里，彻底告别嵌套 switch。",
                "产线主控状态机（待机 / 自动运行 / 暂停 / 故障报警 / 维保点动）与 PLCopen 伺服轴状态机（Disabled → Standstill → DiscreteMotion → ErrorStop）。",
                "本页 Tab 2 实现 5 态 EquipmentStateMachine 与状态图画布；运动控制模块亦落地了完整 PLCopen 轴状态机。",
                """
                public interface IEquipmentState
                {
                    StateTransitionResult Handle(EquipmentEventKind evt);
                }
                // FaultState 内部明确拒绝 Start，只允许 ResetAlarm 回到 Idle
                public StateTransitionResult Handle(EquipmentEventKind evt) => evt switch
                {
                    EquipmentEventKind.ResetAlarm => Allow(EquipmentStateKind.Idle),
                    _ => Reject("故障未复位，严禁直接启动！"),
                };
                """,
                "工业安全联锁最佳实践：所有危险操作（如运行中切维保、故障未复位强行启动）都应在对应 State 类内部显式拒绝，而非指望前端禁用按钮。",
                1),

            new("chain", "责任链模式", "Chain of Responsibility",
                PatternCategory.Behavioral, "行为型",
                "将多级校验或处理工序串成链表，每个节点只关心自身职责：通过则移交下一节点，异常则立即短路拦截并记录原因。",
                "工站过站防呆流水线：条码防重 → 尺寸公差 → 外观复检 → MES 过站；或工控机多级报警过滤器。",
                "本页 Tab 1 的 BarcodeDedupHandler → DimensionToleranceHandler → CosmeticVisionHandler → MesUploadHandler 链式实测。",
                """
                h1.SetNext(h2).SetNext(h3).SetNext(h4);
                // 任一环节失败即标记短路，后续节点自动跳过
                public void Handle(WorkpieceInspectionContext ctx)
                {
                    if (!Evaluate(ctx)) { ctx.IsPassed = false; return; }
                    _next?.Handle(ctx);
                }
                """,
                "责任链非常契合工艺工序可配置化需求：新机型加一道“气密性测试”只需插入一个 Handler，无需修改其他工站校验代码。",
                0),

            new("command", "命令模式", "Command",
                PatternCategory.Behavioral, "行为型",
                "把操作请求封装为独立命令对象，支持队列调度、防重入取消、日志审计以及可逆的撤销 / 重做 (Undo/Redo) 栈。",
                "WPF 按钮绑定 ICommand / AsyncRelayCommand；工艺配方参数调整与示教点位编辑的 Undo / Redo 撤销栈。",
                "WPF 全站使用 [RelayCommand]；本页 Tab 2 实现 IRecipeCommand + RecipeCommandManager 双栈实时演示炉温/速度修改的撤销与重做。",
                """
                public interface IRecipeCommand
                {
                    void Execute(RecipeTarget target);
                    void Undo(RecipeTarget target);
                }
                // Execute 入 Undo 栈并清空 Redo 栈；Undo 弹出执行逆操作并压入 Redo 栈
                """,
                "WPF 上位机高频坑：异步耗时命令务必使用 AsyncRelayCommand（自带并发防重入与 CancellationToken），严禁写 async void 按钮事件！",
                1),

            new("observer", "观察者 / 发布-订阅", "Observer / EventBus",
                PatternCategory.Behavioral, "行为型",
                "定义对象间一对多依赖：当设备状态变更或触发报警时，发布者无需硬引用具体模块，自动广播给所有已订阅观察者。",
                "设备急停报警同时触发：UI 报警横幅刷新 + 三色声光灯红闪 + SQLite 审计落盘 + MES 异常停机上报。",
                "本页 Tab 2 支持动态增减 4 路观察者并实时展示扇出通知；MVVM 页面则用 WeakReferenceMessenger 演示弱引用跨页通信。",
                """
                // 传统 C# event 需记得 -= 解绑，否则短生命周期 ViewModel 会被长生命周期 Service 拖住造成内存泄漏！
                // WPF 跨模块推荐 WeakReferenceMessenger：
                WeakReferenceMessenger.Default.Register<AlarmMessage>(this, (r, m) => r.OnAlarm(m));
                """,
                "WPF 面试必杀题：C# 原生 event 的最大坑是什么？长生命周期发布者持有短生命周期页面的强引用委托导致内存泄漏！解法：Dispose 显式 -= 或改用 WeakReferenceMessenger。",
                1),
        ];
    }
}
