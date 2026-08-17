# Packaging

本目录负责构建应用 Setup 和模型 Release。版本、资产和许可证规则见 [发布与许可](../docs/release.md)。

## 1. 前置环境

应用 Setup 需要：

- .NET 10 SDK；
- 已还原的 NuGet 依赖；
- Inno Setup 6 `ISCC.exe`。

模型 Release 需要已审核的两个 PT、两个 ONNX 和无 `TBD` 的模型卡。

## 2. 构建应用

从仓库根目录执行：

```powershell
.\packaging\build.ps1 -Version X.Y.Z -Repository owner/name
```

输出：

```text
releases/app-vX.Y.Z/VRC-Fisher-Setup-x64.exe
```

构建脚本会：

- 发布一套 DirectML `win-x64` self-contained 程序；
- 编译 20 种语言的 Inno Setup；
- 收集必需许可证；
- 排除 ONNX、PDB 和 `DirectML.Debug.dll`；
- 拒绝缺失运行依赖或法律文件的产物。

Setup 内的 DirectML 运行时同时支持软件的 `Auto / GPU / CPU` 选择，不再生成 CPU-only Setup。

## 3. 构建模型版本

```powershell
.\packaging\build-model-release.ps1 `
  -Version X.Y.Z `
  -Locator training\exports\locator.onnx `
  -Minigame training\exports\minigame.onnx `
  -LocatorCheckpoint training\runs\<locator-run>\weights\best.pt `
  -MinigameCheckpoint training\runs\<minigame-run>\weights\best.pt `
  -ModelCard training\MODEL_CARD.md
```

输出分为两套：

```text
models/vX.Y.Z/          源码仓库：PT、ONNX、模型卡、许可证、来源清单
releases/models-vX.Y.Z/ 软件下载安装：ONNX、模型卡、许可证、运行时清单
```

脚本会检查文件完整性、大小、SHA-256、模型卡占位符、许可证和运行时清单。必须先提交并推送 `models/vX.Y.Z/`，再上传对应 GitHub Release 资产。

## 4. 发布前检查

- 应用和模型版本号正确；
- Release 资产来自对应本地输出目录；
- Setup 不含模型；
- 模型 Release 不含训练环境或 `runs/`；
- GitHub 自动显示的 SHA-256 与本地资产一致；
- 当前 [缺陷发布门禁](../docs/bug.md#发布门禁) 已满足。
