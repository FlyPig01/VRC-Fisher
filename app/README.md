# App

此目录将承载 C#/.NET 10/WinUI 3 正式软件。

## 当前状态

正式 C# 工程位于 `src/`，包括 Core、Application、Infrastructure 和 Desktop；`VrcFisher.sln` 已建立。目录中保留的 Python 原型只能用于移植和对照，不能打包发布。当前正式模型、真实捕获适配和 Setup 仍未完成。

旧文件的存在不代表正式软件需要 Python、MSS、TOML 或 PyInstaller。后续代码阶段应建立 C# 解决方案，并在验证等价能力后清理旧实现。

## 目标入口

```powershell
Set-Location E:\MyTools\VRC-Fisher\app
dotnet restore VrcFisher.sln
dotnet build VrcFisher.sln -c Debug
dotnet test VrcFisher.sln -c Debug
```

开发回放使用的两个已审核模型放在 `app/models/`；当前没有可提交的正式模型，模型文件不提交 Git。

技术栈、架构、性能和发布规则统一见 [开发文档](../docs/README.md)。
