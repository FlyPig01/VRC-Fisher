# App

此目录承载 C#/.NET 10/WinUI 3 正式软件。

## 当前状态

正式 C# 工程位于 `src/`，包括 Core、Application、Infrastructure 和 Desktop。模型管理与更新检查、CPU/DirectML Provider、VRChat 进程限定捕获、四类双模型推理、受限自动调频、状态机、默认禁用的屏外感叹号兜底、可确认更改的全局热键、15 Hz 调试覆盖层和 20 种语言 WinUI 已接入；自包含发布和 Inno Setup 已实际构建、安装并启动。自动调频分别记录 locator、双模型和缓存小游戏 P95，不额外执行推理，并将硬件、Provider、模型版本和分辨率画像写入安装目录的 `config/performance-profiles.json`。候选 ONNX 已通过 CPU/DirectML 加载与真实全屏帧推理，正式模型、真实 VRChat 资源占用和自动流程仍待人工验收。

正式软件不需要 Python、MSS、TOML、PyInstaller 或 CUDA。Python 只存在于仓库的离线数据处理与训练目录，不进入发布包。`models/v0.1.0/` 是源码公开的模型版本；安装目录只放用户选择安装的 ONNX 运行时副本。

本目录的 VRC-Fisher 原创代码采用根目录 MIT License。第三方运行库遵循各自许可证；官方 Ultralytics 衍生 ONNX 模型单独按上游标注的 AGPL-3.0 发布，不属于本目录的 MIT 授权。换用其他模型时必须遵守该模型自己的来源和许可证。

## 目录职责

```text
src/
  VrcFisher.Core/            领域状态、检测契约和输入安全规则
  VrcFisher.Application/     运行用例、配置和模型目录接口
  VrcFisher.Infrastructure/  ONNX、模型下载、捕获源和 Win32 输入实现
  VrcFisher.Desktop/
    Pages/                    运行、模型、设置页面；只编排界面交互
    Ui/                       共享样式与控件构造
    Localization/             本地化访问
    Contracts/                页面可使用的桌面能力边界
    Capture/                  Windows Graphics Capture 平台适配
    Overlay/                  VRChat 点击穿透提示、识别框和 15 Hz 绘制
    App.*                     依赖组装与应用生命周期
    MainWindow.*              固定导航壳和页面切换
```

依赖只能由 Desktop/Infrastructure 指向 Application/Core。页面不持有 ONNX、下载器、Win32 输入或捕获实现，只通过 `IDesktopPageContext` 使用运行、模型、捕获选择和设置保存能力。

## 目标入口

```powershell
$Repo = "C:\path\to\VRC-Fisher"
Set-Location (Join-Path $Repo "app")
dotnet restore VrcFisher.sln
dotnet build VrcFisher.sln -c Debug
dotnet test VrcFisher.sln -c Debug
```

开发回放使用的两个 ONNX 位于 `app/models/`，合计 20.43 MiB，并由 `.gitignore` 排除；它们是 `models/v0.1.0/` 的本地运行副本。GUI 的模型目录必须具有通过校验的 `installed-models.json`、`MODEL_CARD.md` 和 `MODEL_LICENSE.txt` 才会启用观察；`automatic_allowed=true` 后才会启用自动运行。当前 `0.1.0` 清单保持 `automatic_allowed=false`。

首次部署见 [开发环境部署](../docs/development-setup.md)；技术栈、架构、性能和发布规则统一见 [开发文档](../docs/README.md)。
