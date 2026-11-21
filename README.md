# LearnSmartManufacturing

面向智能制造 / 工业自动化上位机开发的 WPF 学习与实践项目，基于 **.NET 11 / WPF** 构建，结合 Fluent 主题、MVVM 与依赖注入，围绕工业现场常用技术逐步展开可视化学习工具与交互仿真。

## 模块进度

| 分组 | 模块 | 状态 | 说明 |
| --- | --- | --- | --- |
| 机器视觉 | 工业相机 | 已完成 | 按像素精度与目标尺寸反推所需分辨率与推荐面阵相机 |
| 机器视觉 | 镜头焦距 | 已完成 | 相似三角形光路动态演示（支持键盘/滑块/预设）与焦距反推计算器 |
| 机器视觉 | 相机仿真 | 已完成 | 薄透镜景深 (DOF) 与光路仿真、可拖拽工件、Z 轴局部剖面与离焦成像预览 |
| 系统 | 首页 / 设置 | 已完成 | 模块概览、WPF Fluent 浅色 / 深色 / 跟随系统主题实时切换 |
| WPF | 异步编程 / MVVM | 规划中 | 占位页面 |
| 协议 | Modbus / EtherCAT / MQTT | 规划中 | 占位页面 |
| 运动控制 | 运动控制 | 规划中 | 占位页面 |

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
