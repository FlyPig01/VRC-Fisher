# Packaging

此目录承载 C# self-contained publish 与单一 Inno Setup 安装器的构建文件。

## 当前状态

正式发布链已经切换为 C#：`build-installer.ps1` 分别发布 CPU-only 与 DirectML 两套 `win-x64` 程序目录，再由一个 `installer.iss` 生成 Setup。旧的 Python/PyInstaller 入口已经移除。

构建示例：

```powershell
.\packaging\build.ps1 -Version 0.1.0 -Repository owner/name
```

本机需要 .NET SDK、Windows App SDK NuGet 依赖和 Inno Setup 6 `ISCC.exe`。简体中文安装器翻译已作为构建资源固定在 `languages/`，最终用户不下载语言包。脚本每次同时构建 CPU-only 与 DirectML 两套程序源，拒绝把 `.onnx` 放进 Setup，并在 `releases/app-vX.Y.Z/` 输出一个安装器和 SHA-256。安装器、模型 Release、更新与卸载契约见 [安装与发布设计](../docs/installation-and-release.md)。
