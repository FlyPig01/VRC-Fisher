# App

此目录承载 C#/.NET 10/WinUI 3 正式软件。

## 当前状态

正式 C# 工程位于 `src/`，包括 Core、Application、Infrastructure 和 Desktop。模型管理、CPU/DirectML Provider、完整显示器捕获、双模型推理、状态机和中英文 WinUI 已接入；自包含发布和 Inno Setup 已实际构建、安装并启动。正式模型和真实场景验收仍受未标注数据阻塞。

正式软件不需要 Python、MSS、TOML、PyInstaller 或 CUDA。Python 只存在于仓库的离线数据处理与训练目录，不进入发布包。

本目录的 VRC-Fisher 原创代码采用根目录 MIT License。第三方运行库遵循各自许可证；官方 Ultralytics 衍生 ONNX 模型单独按上游标注的 AGPL-3.0 发布，不属于本目录的 MIT 授权。换用其他模型时必须遵守该模型自己的来源和许可证。

## 目标入口

```powershell
$Repo = "C:\path\to\VRC-Fisher"
Set-Location (Join-Path $Repo "app")
dotnet restore VrcFisher.sln
dotnet build VrcFisher.sln -c Debug
dotnet test VrcFisher.sln -c Debug
```

开发回放使用的两个审核后 ONNX 应放在 `app/models/`；当前目录没有正式模型，模型缺失时应用可以启动但不能开始识别，模型文件不提交 Git。

首次部署见 [开发环境部署](../docs/development-setup.md)；技术栈、架构、性能和发布规则统一见 [开发文档](../docs/README.md)。
