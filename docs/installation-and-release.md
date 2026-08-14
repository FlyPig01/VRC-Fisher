# 安装与发布设计

> 当前状态：`dotnet publish -> Inno Setup` 已实际生成单一 Setup，并完成 CPU-only、DirectML、简体中文和 English 的安装启动验证；正式 ONNX、真实 VRChat 验收、代码签名和已上传的 GitHub Release 仍未完成。语言资源不是下载项。

## 1. 发布物

应用 Release 只提供一个完整安装器：

```text
app-vX.Y.Z/
  VRC-Fisher-Setup-x64.exe
  VRC-Fisher-Setup-x64.exe.sha256
```

每个正式模型首先完整保存在源码仓库：

```text
models/vX.Y.Z/
  checkpoints/locator.pt
  checkpoints/minigame.pt
  runtime/locator.onnx
  runtime/minigame.onnx
  source-manifest.json
  MODEL_CARD.md
  MODEL_LICENSE.txt
```

模型同时使用独立 Release 作为最终用户下载渠道：

```text
models-vX.Y.Z/
  locator.onnx
  minigame.onnx
  model-manifest.json
  MODEL_CARD.md
  MODEL_LICENSE.txt
```

模型不压进 Setup。这样用户可以独立下载、更新和删除模型，而不必重新安装软件；应用升级也不会覆盖模型。Release 不是模型唯一的开源位置：两个 `.pt` 和两个 ONNX 必须已存在于相同 Git 标签的 `models/vX.Y.Z/`，Release 资产只能是其中运行时文件的逐字节相同副本。

这个 Setup 不是几 MB 的联网引导程序。它离线包含 C# 软件本体、.NET/WinUI 运行依赖以及 CPU-only、DirectML 两种安装源；本机实测 Setup 约 `113 MB`。其他软件的几 MB Setup 通常只负责联网下载本体，本项目首版不采用该方式。

## 2. 语言资源从哪里来

语言资源的来源是仓库中的以下文件，而不是 GitHub 下载：

```text
app/src/VrcFisher.Desktop/Strings/zh-CN/Resources.resw
app/src/VrcFisher.Desktop/Strings/en-US/Resources.resw
```

WinUI 构建把 `.resw` 编译为应用的 `VrcFisher.pri`，`dotnet publish` 将该资源和程序一起放入发布目录，Inno Setup 再把发布目录复制到用户选择的安装目录。因此首次安装不需要网络就能取得中英文资源；模型下载是唯一的运行时大文件下载。

Setup 的英文来自 Inno Setup 6 自带 `Default.isl`；简体中文安装器翻译来自 Inno Setup 官方 `jrsoftware/issrc` 仓库，已固定保存在 `packaging/languages/ChineseSimplified.isl`，因为 Inno Setup 安装程序默认不安装该文件。两者在构建时进入 Setup。Setup 的语言页同时写入 `<安装目录>\\config\\installer-language.ini`；应用启动时读取这个文件，选择对应的内置 `.pri` 资源。这些语言资源都不在最终用户电脑上联网下载。变更语言后重启应用生效；新增语言需要重新发布软件，首版不提供独立语言包。

## 3. 构建链

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

## 4. 安装向导

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

安装器语言同时作为软件首次启动语言。简体中文和 English `.resw` 已随 Desktop 工程编译进应用；当前版本在安装器选择语言，修改后重启应用生效，不下载单独语言包。

## 5. 运行组件选择

| 组件 | 安装内容 | 软件内设备选项 |
|---|---|---|
| CPU-only | 标准 ONNX Runtime | `Auto`、`CPU` |
| DirectML | ONNX Runtime DirectML | `Auto`、`CPU`、`GPU` |

Setup 默认推荐 DirectML，但保留 CPU-only。CPU-only 依赖更少，在没有可用 DirectX 12 GPU、GPU 争用严重或 DirectML 不稳定时可能表现更好。

重新运行 Setup 可以切换组件。安装器必须先停止 VRC-Fisher，清理旧的程序文件和原生运行库，再安装所选组件；`models/`、`config/user.json`、`config/performance-profiles.json`、`logs/` 和 `artifacts/` 不受影响。Provider 变化后旧性能画像不会匹配，软件会自动重新采样。

两个组件使用相同 ONNX。切换组件不重新训练、不转换也不重复下载模型。当前不提供 CUDA 组件。

## 6. 安装目录契约

```text
<安装目录>/
  release.json
  USER_GUIDE.md
  LICENSE
  THIRD_PARTY_NOTICES.md
  licenses/
    AGPL-3.0.txt
    third-party/
  program/
    VrcFisher.exe
  config/
    user.json
    performance-profiles.json
    installer-language.ini
  models/
    locator.onnx
    minigame.onnx
    MODEL_CARD.md
    MODEL_LICENSE.txt
    installed-models.json
  downloads/
  logs/
    vrc-fisher.log
  artifacts/
    runtime-metrics.json
    failures/
```

应用通过 `program\\VrcFisher.exe` 的父目录识别安装根目录。程序文件放在 `program/`，模型、配置、日志、指标、截图或下载暂存仍全部位于用户选定的安装目录；禁止写入 `%LOCALAPPDATA%`、`ProgramData`、用户主目录或其他隐藏位置。

`USER_GUIDE.md` 从仓库根目录原样包含进安装包，保证 GitHub 与软件目录中的手册一致。Setup 同时携带原创代码 MIT、第三方声明、AGPL-3.0 和实际发布依赖包要求的许可证/NOTICE；构建脚本找不到这些法律文件时必须停止。

## 7. 模型下载与删除

安装时勾选模型不会把模型变成 Setup 内置文件。Setup 安装完软件后调用 C# 模型管理命令：

```powershell
program\\VrcFisher.exe --download-models --non-interactive
```

这个命令与应用内“下载模型”共用同一服务和事务流程：

```text
查询兼容的 models-v* Release
  -> 下载 model-manifest.json
  -> 验证清单版本与 runtime_api
  -> 下载两个 ONNX、模型卡和模型许可证到 <安装目录>/downloads/
  -> 验证四个文件的大小与 SHA-256
  -> 四个文件全部成功后原子替换 models/
  -> 写入 installed-models.json
```

瞬时网络错误和 HTTP 408、429、5xx 最多尝试三次；用户取消、清单错误、哈希不一致或最终下载失败时，删除本次暂存并保留旧模型。绝不能先删旧模型再下载新模型。

非交互命令以 `0` 表示两个模型均已可用，以非零值表示下载或校验失败。模型下载失败不回滚已经安装的软件；Setup 显示失败原因，并告知用户之后可在“模型”页面重试。

软件“模型”页面必须支持：

- 查看已安装版本、文件大小和校验状态；
- 下载或更新两个兼容模型；
- 取消和重试下载；
- 删除两个 ONNX、模型卡、模型许可证与模型记录，但保留软件；
- 模型缺失时重新下载。

两个模型及其模型卡、模型许可证构成一个不可分割的模型版本。首版不允许只更新其中一个文件，以免类别、输入尺寸、后处理契约或许可证记录不匹配。任一文件缺失或校验失败时软件可以打开，但“仅观察”和“自动运行”保持禁用。

清单至少包含：

```json
{
  "schema_version": 2,
  "runtime_api": 1,
  "version": "1.0.0",
  "automatic_allowed": false,
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
  ],
  "documentation": [
    {
      "filename": "MODEL_CARD.md",
      "size": 12345,
      "sha256": "64-character-lowercase-hex"
    },
    {
      "filename": "MODEL_LICENSE.txt",
      "size": 34567,
      "sha256": "64-character-lowercase-hex"
    }
  ]
}
```

## 8. GitHub Releases 协议

应用版本和模型版本独立。应用只选择满足 `runtime_api` 的最新非草稿、非预发布 `models-v*` Release。GitHub Releases API 是首版软件内唯一下载源，不设计 CDN 或镜像自动切换；源码使用者可以直接从相同标签的 `models/vX.Y.Z/` 取得 `.pt` 和 ONNX。发布模型 Release 前必须确认仓库目录已提交并推送，且 Release 中两个 ONNX 的 SHA-256 与 `source-manifest.json` 一致。

构建时生成 `release.json`，记录仓库 `owner/name`、应用版本和 `runtime_api`。模型清单中的 `automatic_allowed` 必须由人工验收后才可设为 `true`；缺少该字段或为 `false` 时，软件仍可仅观察，但拒绝自动输入。当前 C# 下载器使用 10 分钟 HTTP 请求超时、最多三次瞬时故障尝试、取消令牌、独立暂存目录以及大小和 SHA-256 校验。

模型构建脚本要求显式传入已填写的模型卡。模型卡中仍有 `TBD`，没有上游标注的 `AGPL-3.0`，或缺少发布版本、两个 `.pt` 与两个 ONNX 的实际大小和 SHA-256 时，脚本必须停止。模型 Release 还必须提供与 `training/LICENSE` 一致的 `MODEL_LICENSE.txt`，并在同一项目中提供对应版本完整源码。构建脚本将模型卡与许可证的大小和 SHA-256 写入 schema v2 清单；当前应用拒绝缺少这两个侧车文件的旧清单。

当前模型按普通 Git 二进制文件提交，`.gitattributes` 禁止把 `.pt` 和 ONNX 当作文本比较。构建脚本要求四个模型文件各自小于 100 MiB，避免 GitHub 拒绝推送；YOLO11n 满足这一门槛。未来模型超过门槛时必须先正式引入 Git LFS，并让克隆、源码归档和离线构建流程都验证 LFS 对象完整，不能只删除大小检查。

## 9. 应用升级

首版不实现自动更新器、版本自动检查、增量补丁或后台静默更新。用户从项目的 `app-v*` Release 页面下载并运行新版 Setup：

```text
运行新版 Setup
  -> 识别现有 AppId 与安装目录
  -> 停止正在运行的软件
  -> 替换程序、.NET、WinUI 和 ONNX Runtime 文件
  -> 保留 models、user.json、logs 和 artifacts
```

只更新少量文件需要单独的差分打包、版本清单、回滚和签名系统，明显增加首版复杂度，因此暂不实现。模型已经独立发布，不会因软件更新重复下载。

## 10. 卸载

正常卸载删除安装器登记的软件文件，以及安装目录内由 VRC-Fisher 创建的模型、配置、日志、下载暂存和诊断文件。安装器不递归删除安装目录中的其他文件。卸载前应明确提示这些数据也会被删除。

安装器只允许空目录或已识别的软件目录，因此卸载不会递归删除用户无关文件。只想释放模型空间时，用户应在软件“模型”页面删除模型，而不是卸载软件。

## 11. 发布验收

1. 只生成一个 Setup，并提供简体中文和 English。
2. 用户能选择任意可写安装目录，全部运行数据只写入该目录。
3. CPU-only 与 DirectML 互斥安装，切换后没有旧原生 DLL 残留。
4. 干净 Windows x64 环境无需预装 .NET、Python 或 CUDA 即可启动。
5. Setup 不包含 ONNX、Python、PyTorch、Ultralytics、数据集或录屏。
6. 未安装模型时 GUI 可启动，但识别和输入均禁用。
7. 模型与模型卡/许可证下载失败不破坏旧版本，并可取消、重试和一并删除。
8. 覆盖安装保留用户数据，卸载只删除软件拥有的目录内容。
9. Setup、安装后体积和所有运行资源均以正式构建实测。
10. Setup 携带 MIT、第三方声明、AGPL 和实际依赖的许可证/NOTICE。
11. 对应 Git 标签的 `models/vX.Y.Z/` 携带两个 `.pt`、两个 ONNX、无占位符模型卡、AGPL 全文和源码清单。
12. 模型 Release 携带运行时清单与仓库逐字节一致的两个 ONNX，并提供同标签源码入口。

容量门槛与完整测试矩阵见 [性能与存储预算](performance-budget.md)。
