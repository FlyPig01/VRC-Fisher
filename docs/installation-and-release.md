# 安装与发布设计

> 本文是目标设计。C# 工程已经建立，但仓库当前的 `packaging/` 仍是 Python/PyInstaller 旧实现，不能生成这里定义的 C# 正式安装包；正式语言资源随 WinUI 本体内置，不是单独 Release 下载项。

## 1. 发布物

应用 Release 只提供一个完整安装器：

```text
app-vX.Y.Z/
  VRC-Fisher-Setup-x64.exe
  VRC-Fisher-Setup-x64.exe.sha256
```

模型使用独立 Release：

```text
models-vX.Y.Z/
  locator.onnx
  minigame.onnx
  model-manifest.json
```

模型不压进 Setup。这样用户可以独立下载、更新和删除模型，而不必重新安装软件；应用升级也不会覆盖模型。

这个 Setup 不是几 MB 的联网引导程序。它离线包含 C# 软件本体、.NET/WinUI 运行依赖以及 CPU-only、DirectML 两种安装源，因此预计为数百 MB。其他软件的几 MB Setup 通常只负责联网下载本体，本项目首版不采用该方式。

## 2. 构建链

同一份 C# 源码生成两个隔离的自包含目录：

```text
dotnet publish: CPU-only
  -> Microsoft.ML.OnnxRuntime

dotnet publish: DirectML
  -> Microsoft.ML.OnnxRuntime.DirectML

两个 publish 目录
  -> Inno Setup 6
  -> 一个 VRC-Fisher-Setup-x64.exe
```

发布基线：

```text
TargetFramework: net10.0-windows10.0.19041.0
RuntimeIdentifier: win-x64
SelfContained: true
WindowsPackageType: None
WindowsAppSDKSelfContained: true
PublishSingleFile: false
PublishTrimmed: false
```

CPU-only 和 DirectML 必须输出到不同目录，不能混装两个 ONNX Runtime NuGet 包。Inno Setup 将两套目录压入一个 Setup，并根据安装选择只释放其中一套。

首版不启用单文件或 trimming。WinUI 3、Windows App SDK、反射和原生 ONNX DLL 都可能受裁剪影响；只有完整回归证明安全后才评估缩减体积。

## 3. 安装向导

同一个 Setup 按以下顺序执行：

```text
选择简体中文或 English
  -> 选择安装目录
  -> 选择 CPU-only 或 DirectML
  -> 选择是否立即下载必需模型
  -> 选择是否创建快捷方式
  -> 安装所选软件组件
  -> 可选：调用已安装程序下载模型
  -> 完成
```

安装器必须显示标准目录选择页。Setup 文件位于哪里与软件安装在哪里无关；用户可以把下载到 `C:\Downloads` 的 Setup 安装到 `E:\Apps\VRC-Fisher`。最终选择的目录是软件根目录，模型、配置、日志、诊断与下载暂存均放在其中。

安装目录必须满足：

- 当前用户可写；
- 是空目录，或是由稳定 `AppId` 与 `release.json` 识别出的现有 VRC-Fisher 目录；
- 不能把一个包含无关文件的既有目录当作软件根目录；
- 覆盖安装时沿用原目录并保留用户数据。

安装器语言同时作为软件首次启动语言。简体中文和 English `.resw` 已随 Desktop 工程编译进应用；软件内仍可随时切换语言，不下载单独语言包。

## 4. 运行组件选择

| 组件 | 安装内容 | 软件内设备选项 |
|---|---|---|
| CPU-only | 标准 ONNX Runtime | `Auto`、`CPU` |
| DirectML | ONNX Runtime DirectML | `Auto`、`CPU`、`GPU` |

Setup 默认推荐 DirectML，但保留 CPU-only。CPU-only 依赖更少，在没有可用 DirectX 12 GPU、GPU 争用严重或 DirectML 不稳定时可能表现更好。

重新运行 Setup 可以切换组件。安装器必须先停止 VRC-Fisher，清理旧的程序文件和原生运行库，再安装所选组件；`models/`、`config/user.json`、`logs/` 和 `artifacts/` 不受影响。

两个组件使用相同 ONNX。切换组件不重新训练、不转换也不重复下载模型。当前不提供 CUDA 组件。

## 5. 安装目录契约

```text
<安装目录>/
  VrcFisher.exe
  runtime/
  config/
    default.json
    user.json
  models/
    locator.onnx
    minigame.onnx
    installed-models.json
  downloads/
  logs/
    vrc-fisher.log
  artifacts/
    runtime-metrics.json
    failures/
  release.json
  USER_GUIDE.md
```

应用通过 `VrcFisher.exe` 所在目录确定根目录。禁止把模型、配置、日志、指标、截图或下载临时文件写入 `%LOCALAPPDATA%`、`ProgramData`、用户主目录或其他隐藏位置。

`USER_GUIDE.md` 从仓库根目录原样包含进安装包，保证 GitHub 与软件目录中的手册一致。

## 6. 模型下载与删除

安装时勾选模型不会把模型变成 Setup 内置文件。Setup 安装完软件后调用 C# 模型管理命令：

```powershell
VrcFisher.exe --download-models --non-interactive
```

这个命令与应用内“下载模型”共用同一服务和事务流程：

```text
查询兼容的 models-v* Release
  -> 下载 model-manifest.json
  -> 验证清单版本与 runtime_api
  -> 下载两个 ONNX 到 <安装目录>/downloads/
  -> 验证文件大小与 SHA-256
  -> 两个文件全部成功后原子替换 models/
  -> 写入 installed-models.json
```

网络中断、取消、清单错误或哈希不一致时，删除本次暂存并保留旧模型。绝不能先删旧模型再下载新模型。

非交互命令以 `0` 表示两个模型均已可用，以非零值表示下载或校验失败。模型下载失败不回滚已经安装的软件；Setup 显示失败原因，并告知用户之后可在“模型”页面重试。

软件“模型”页面必须支持：

- 查看已安装版本、文件大小和校验状态；
- 下载或更新两个兼容模型；
- 取消和重试下载；
- 删除两个 ONNX 与模型记录，但保留软件；
- 模型缺失时重新下载。

两个模型构成一个不可分割的模型版本。首版不允许只更新其中一个，以免类别、输入尺寸或后处理契约不匹配。模型缺失或校验失败时软件可以打开，但“仅观察”和“自动运行”保持禁用。

清单至少包含：

```json
{
  "schema_version": 1,
  "runtime_api": 1,
  "version": "1.0.0",
  "models": [
    {
      "filename": "locator.onnx",
      "size": 12345678,
      "sha256": "64-character-lowercase-hex"
    },
    {
      "filename": "minigame.onnx",
      "size": 12345678,
      "sha256": "64-character-lowercase-hex"
    }
  ]
}
```

## 7. GitHub Releases 协议

应用版本和模型版本独立。应用只选择满足 `runtime_api` 的最新非草稿、非预发布 `models-v*` Release。GitHub Releases API 是首版唯一下载源，不设计 CDN 或镜像自动切换。

构建时生成 `release.json`，至少记录仓库 `owner/name`、应用版本、安装组件和 `runtime_api`。下载请求必须支持取消、连接与整体超时、重试上限、临时文件以及 SHA-256 校验。

## 8. 应用升级

首版不实现自动更新器、增量补丁或后台静默更新。软件可以提示新版并打开 `app-v*` Release 页面，但实际升级由用户下载并运行新版 Setup：

```text
运行新版 Setup
  -> 识别现有 AppId 与安装目录
  -> 停止正在运行的软件
  -> 替换程序、.NET、WinUI 和 ONNX Runtime 文件
  -> 保留 models、user.json、logs 和 artifacts
```

只更新少量文件需要单独的差分打包、版本清单、回滚和签名系统，明显增加首版复杂度，因此暂不实现。模型已经独立发布，不会因软件更新重复下载。

## 9. 卸载

正常卸载删除安装器登记的软件文件，以及安装目录内由 VRC-Fisher 创建的模型、配置、日志、下载暂存和诊断文件。卸载前应明确提示这些数据也会被删除。

安装器只允许空目录或已识别的软件目录，因此卸载不会递归删除用户无关文件。只想释放模型空间时，用户应在软件“模型”页面删除模型，而不是卸载软件。

## 10. 发布验收

1. 只生成一个 Setup，并提供简体中文和 English。
2. 用户能选择任意可写安装目录，全部运行数据只写入该目录。
3. CPU-only 与 DirectML 互斥安装，切换后没有旧原生 DLL 残留。
4. 干净 Windows x64 环境无需预装 .NET、Python 或 CUDA 即可启动。
5. Setup 不包含 ONNX、Python、PyTorch、Ultralytics、数据集或录屏。
6. 未安装模型时 GUI 可启动，但识别和输入均禁用。
7. 模型下载失败不破坏旧版本，并可取消、重试和删除。
8. 覆盖安装保留用户数据，卸载只删除软件拥有的目录内容。
9. Setup、安装后体积和所有运行资源均以正式构建实测。

容量门槛与完整测试矩阵见 [性能与存储预算](performance-budget.md)。
