# App

本目录只包含正式 C# 软件和自动测试。运行架构见 [架构与运行](../docs/architecture.md)。

## 目录

```text
src/
  VrcFisher.Core/            状态机、检测契约、小游戏控制和平台抽象
  VrcFisher.Application/     启停事务、配置和运行状态
  VrcFisher.Infrastructure/  WGC、ONNX、模型下载、文件和 SendInput
  VrcFisher.Desktop/         WinUI 3、页面、本地化、覆盖层和依赖组装
tests/
  VrcFisher.Core.Tests/      核心、基础设施和运行时自动测试
models/                      本地开发模型副本，不提交 Git
```

分层依赖和模块边界统一见 [架构与运行](../docs/architecture.md)。

## 开发命令

从仓库根目录执行：

```powershell
dotnet restore app\VrcFisher.sln
dotnet build app\VrcFisher.sln -c Debug
dotnet test app\VrcFisher.sln -c Debug
dotnet test app\VrcFisher.sln -c Release -p:Platform=x64
```

## 本地运行条件

开发运行需要：

- 可用的 `locator.onnx` 和 `minigame.onnx`；
- 与模型匹配的运行时清单、模型卡和许可证；
- 前台运行的 `VRChat.exe`；
- Windows x64 和 DirectX 12/DirectML 兼容环境。

开发模型副本放在 `app/models/` 并由 `.gitignore` 排除。正式发布模型只能来自已验收的 `models/vX.Y.Z/`。

环境安装见 [开发与环境](../docs/development.md)，小游戏逻辑见 [小游戏控制](../docs/minigame-control.md)。
