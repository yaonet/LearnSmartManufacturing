namespace LearnSmartManufacturing.ViewModels
{
    public class HomeViewModel : PageViewModelBase
    {
        public override string Heading => "首页";

        public string Summary =>
            "Learn Smart Manufacturing 是一个面向智能制造 / 工业自动化上位机开发的学习项目，" +
            "使用 WPF 构建桌面应用，并围绕工业现场常用的通信协议、机器视觉与运动控制逐步展开学习与实践。";

        public IReadOnlyList<ModuleInfo> Modules { get; } =
        [
            new ModuleInfo("WPF", "异步编程、MVVM：构建响应流畅、结构清晰的上位机界面。"),
            new ModuleInfo("协议", "Modbus、EtherCAT、MQTT：设备通信与数据采集、上云。"),
            new ModuleInfo("机器视觉", "工业相机：图像采集与视觉检测基础。"),
            new ModuleInfo("运动控制", "轴运动与控制逻辑的上位机实现。"),
        ];

        public IReadOnlyList<string> TechStack { get; } =
        [
            ".NET 11 / WPF",
            "CommunityToolkit.Mvvm（MVVM）",
            "Microsoft.Extensions.Hosting（依赖注入）",
            "WPF Fluent 主题（浅色 / 深色 / 跟随系统）",
        ];
    }
}
