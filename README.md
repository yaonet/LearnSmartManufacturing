# LearnSmartManufacturing

面向智能制造 / 工业自动化上位机开发的 WPF 学习与实践项目，基于 **.NET 11 / WPF** 构建，结合 Fluent 主题、MVVM 与依赖注入，围绕工业现场常用技术逐步展开可视化学习工具与交互仿真。

## 模块进度

| 分组 | 模块 | 状态 | 说明 |
| --- | --- | --- | --- |
| 机器视觉 | 相机 | 已完成 | 分辨率/视野余量选型、10 款主流 CMOS 靶面与镜头像圆暗角匹配画布、Global/Rolling 快门畸变仿真与 5 接口带宽计算 |
| 机器视觉 | 镜头 | 已完成 | 相似三角形光路、焦距反推与微距延长接圈计算、9 焦段选型表、普通 FA vs 远心镜头透视误差、径向畸变仿真与 C/CS/F 法兰距校验 |
| 机器视觉 | 光源 | 已完成 | 6 种工业光源明/暗场光路射线仿真、黑白相机成像与扫描线剖面、光谱互补色轮、正交偏振与飞拍频闪计算器 |
| 机器视觉 | 仿真 | 已完成 | 薄透镜景深 (DOF) 与光路仿真、可拖拽工件、Z 轴局部剖面与离焦成像预览 |
| 机器视觉 | 标定 | 已完成 | 九点手眼标定（Eye-to-Hand / Eye-in-Hand 最小二乘仿射矩阵、离群点剔除、点击验证）与旋转中心圆拟合/先转后补纠偏 |
| 系统 | 首页 / 设置 | 已完成 | 模块概览、WPF Fluent 浅色 / 深色 / 跟随系统主题实时切换 |
| WPF | 异步编程 | 已完成 | 60 FPS UI 心跳/死锁对照、BoundedChannel 背压仿真、SemaphoreSlim 多设备并发泳道与联动取消 |
| WPF | MVVM | 已完成 | 零业务 Code-Behind：`ObservableValidator` 跨属性校验、`AsyncRelayCommand` 防重入/取消、`WeakReferenceMessenger` 双向解耦与数据流追踪 |
| 协议 | Modbus | 已完成 | 手写 Modbus TCP (MBAP) / RTU (CRC-16) 编解码、帧解剖画布、ABCD/CDAB/DCBA/BADC 字节序、内置模拟 PLC 轮询与真实 Socket 客户端 |
| 协议 | MQTT | 已完成 | MQTT 3.1.1 二进制编解码、QoS 0/1/2 握手时序画布（含丢包/DUP 重传）、ISA-95 通配符演练场、Retain/LWT、断网续传与真实 TCP 客户端 |
| 协议 | OPC UA | 已完成 | UACP 二进制头编解码、X.509 证书信任拦截 (0x801A0000)、地址空间节点树 Read/Write/Method RPC、Subscription 死区过滤示波与 KeepAlive |
| 协议 | PLC 协议 | 已完成 | 西门子 S7 (TPKT+COTP+S7Comm)、三菱 MC SLMP 3E 二进制与欧姆龙 FINS/TCP 报文编解码解剖、大小端/CDAB/S7String 读写仿真、典型报错帧与 PDU 批量打包优化 |
| 协议 | EtherCAT | 已完成 | EtherCAT 0x88A4 LRW 帧编解码、飞速处理环网与 WKC 校验、ETG.1000 ESM 状态机、CiA 402 伺服 PDO、CoE SDO 邮箱与分布式时钟 (DC) 纳秒抖动示波 |
| 协议 | TCP/串口 | 已完成 | 二进制/ASCII `\r\n`/定长 12B 三协议粘包半包拆包与 EMI 杂波重同步、真实本机 TCP 环回、RS485 半双工防冲突、指数退避重连与波特率 T3.5 计算 |
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
