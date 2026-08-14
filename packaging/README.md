# Packaging

此目录承载 C# self-contained publish 与单一 Inno Setup 安装器的构建文件。

## 当前状态

正式发布链已经切换为 C#：`build-installer.ps1` 分别发布 CPU-only 与 DirectML 两套 `win-x64` 程序目录，再由一个 `installer.iss` 生成 Setup。旧的 Python/PyInstaller 入口已经移除。

构建示例：

```powershell
.\packaging\build.ps1 -Version 0.1.0 -Repository owner/name
```

本机需要 .NET SDK、Windows App SDK NuGet 依赖和 Inno Setup 6 `ISCC.exe`。简体中文安装器翻译已作为构建资源固定在 `languages/`，最终用户不下载语言包。脚本每次同时构建 CPU-only 与 DirectML 两套程序源，拒绝把 `.onnx` 放进 Setup，并在 `releases/app-vX.Y.Z/` 输出一个安装器和 SHA-256。安装器、模型 Release、更新与卸载契约见 [安装与发布设计](../docs/installation-and-release.md)。

Setup 构建会强制收集根目录 MIT、第三方声明、AGPL-3.0 以及已还原的 Windows App SDK、ONNX Runtime、.NET、CommunityToolkit、Logging 和 Inno Setup 法律文件；缺失任何必需文件时停止构建。

Inno Setup 当前许可允许用于商业应用。其 6.7.3 编译器会显示 `Non-commercial use only`，但官方 FAQ 说明商业许可证是请求购买而非严格要求；具体记录见根目录 `THIRD_PARTY_NOTICES.md`。更换编译器版本时必须重新审计并复制新版本随附的 `license.txt`。

模型 Release 需要先从 `training/MODEL_CARD.template.md` 创建并填写一份无 `TBD` 的模型卡：

```powershell
.\packaging\build-model-release.ps1 `
  -Version 0.1.0 `
  -Locator training\exports\locator.onnx `
  -Minigame training\exports\minigame.onnx `
  -LocatorCheckpoint training\runs\locator\weights\best.pt `
  -MinigameCheckpoint training\runs\minigame\weights\best.pt `
  -ModelCard training\MODEL_CARD.md
```

脚本生成两套同源产物：`models/vX.Y.Z/` 保存必须提交到源码仓库的两个 `.pt`、两个 ONNX、模型卡、许可证和 `source-manifest.json`；`releases/models-vX.Y.Z/` 只保存软件下载安装所需的两个 ONNX、模型卡、许可证和 schema v2 运行时清单。缺少任一文件、模型卡仍有 `TBD`、哈希或大小未写进模型卡，或未声明上游标注的 `AGPL-3.0` 时拒绝生成。必须先提交并推送 `models/vX.Y.Z/`，之后才能上传对应 Release 资产。
