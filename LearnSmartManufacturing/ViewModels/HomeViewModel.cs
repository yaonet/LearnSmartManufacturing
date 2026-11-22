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
            new ModuleInfo("设计模式", "工业上位机 10 大高频 GoF 模式：工厂/装饰器/责任链过站流水线、状态机/观察者/策略/命令撤销栈与面试速查。"),
            new ModuleInfo("协议", "Modbus、EtherCAT、MQTT、OPC UA、PLC 私有协议（西门子 S7 / 三菱 MC 3E / 欧姆龙 FINS）与 TCP/串口。"),
            new ModuleInfo("机器视觉", "相机选型与快门/带宽、镜头光路与远心/畸变、光源打光与光谱、景深仿真，以及九点手眼与旋转中心标定。"),
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
