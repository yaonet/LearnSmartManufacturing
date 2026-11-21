using CommunityToolkit.Mvvm.ComponentModel;
using LearnSmartManufacturing.Models;

namespace LearnSmartManufacturing.ViewModels
{
    public class HomeViewModel : ObservableObject
    {
        public string Heading => "首页";

        public string Summary =>
            "Learn Smart Manufacturing 是一个面向智能制造 / 工业自动化上位机开发的学习项目，" +
            "使用 WPF 构建桌面应用，并围绕工业现场常用的通信协议、机器视觉与运动控制逐步展开学习与实践。";

        public IReadOnlyList<ModuleInfo> Modules { get; } =
        [
            new ModuleInfo("WPF", "异步编程（UI 心跳/Channel 背压/限流）与 MVVM（校验/命令/WeakReferenceMessenger 解耦）。"),
            new ModuleInfo("协议", "Modbus（TCP/RTU 帧解剖、CRC-16、字节序与模拟 PLC 轮询）、EtherCAT、MQTT。"),
            new ModuleInfo("机器视觉", "工业相机选型、镜头焦距与视场角光路、景深 (DOF) 与成像仿真。"),
            new ModuleInfo("运动控制", "单轴 PLCopen 状态机与 T/S 规划、XY 双轴直线/圆弧插补、点位示教配方与 PSO 飞拍仿真。"),
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
