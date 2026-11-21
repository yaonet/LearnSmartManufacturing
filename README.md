# LearnSmartManufacturing

面向智能制造 / 工业自动化上位机开发的 WPF 学习与实践项目，基于 **.NET 11 / WPF** 构建，结合 Fluent 主题、MVVM 与依赖注入，围绕工业现场常用技术逐步展开可视化学习工具与交互仿真。

## 模块进度

| 分组 | 模块 | 状态 | 说明 |
| --- | --- | --- | --- |
| 机器视觉 | 相机 | 已完成 | 按像素精度与目标尺寸反推所需分辨率与推荐面阵相机 |
| 机器视觉 | 镜头 | 已完成 | 相似三角形光路动态演示（支持键盘/滑块/预设）与焦距反推计算器 |
| 机器视觉 | 光源 | 已完成 | 6 种工业光源明/暗场光路射线仿真、黑白相机成像与扫描线剖面、光谱互补色轮、正交偏振与飞拍频闪计算器 |
| 机器视觉 | 仿真 | 已完成 | 薄透镜景深 (DOF) 与光路仿真、可拖拽工件、Z 轴局部剖面与离焦成像预览 |
| 系统 | 首页 / 设置 | 已完成 | 模块概览、WPF Fluent 浅色 / 深色 / 跟随系统主题实时切换 |
| WPF | 异步编程 | 已完成 | 60 FPS UI 心跳/死锁对照、BoundedChannel 背压仿真、SemaphoreSlim 多设备并发泳道与联动取消 |
| WPF | MVVM | 已完成 | 零业务 Code-Behind：`ObservableValidator` 跨属性校验、`AsyncRelayCommand` 防重入/取消、`WeakReferenceMessenger` 双向解耦与数据流追踪 |
| 协议 | Modbus | 已完成 | 手写 Modbus TCP (MBAP) / RTU (CRC-16) 编解码、帧解剖画布、ABCD/CDAB/DCBA/BADC 字节序、内置模拟 PLC 轮询与真实 Socket 客户端 |
| 协议 | MQTT | 已完成 | MQTT 3.1.1 二进制编解码、QoS 0/1/2 握手时序画布（含丢包/DUP 重传）、ISA-95 通配符演练场、Retain/LWT、断网续传与真实 TCP 客户端 |
| 协议 | EtherCAT | 已完成 | EtherCAT 0x88A4 LRW 帧编解码、飞速处理环网与 WKC 校验、ETG.1000 ESM 状态机、CiA 402 伺服 PDO、CoE SDO 邮箱与分布式时钟 (DC) 纳秒抖动示波 |
| 运动控制 | 运动控制 | 已完成 | 单轴 HAL + PLCopen 状态机 + T/S 7 段规划示波；XY 双轴 PTP/G01/G02/G03 插补、示教配方与 PSO 飞拍 |

## 技术栈

- **.NET 11 / WPF**（`ThemeMode` Fluent 主题）
- **CommunityToolkit.Mvvm**（`ObservableObject`、`[ObservableProperty]`、`[RelayCommand]`）
- **Microsoft.Extensions.Hosting**（依赖注入与每页独立 `IServiceScope` 生命周期管理）
- **xUnit**（核心光学计算、ViewModel 与导航/主题服务单元测试）

## 构建与运行

```powershell
# 构建解决方案
dotnet build LearnSmartManufacturing.slnx

# 运行应用
dotnet run --project LearnSmartManufacturing/LearnSmartManufacturing.csproj

# 运行单元测试
dotnet test LearnSmartManufacturing.slnx
```
