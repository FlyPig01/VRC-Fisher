# VRC-Fisher

VRC-Fisher 是一个仅面向 Windows 的 VRChat 钓鱼自动化项目。程序捕获用户选择的完整显示器，识别咬钩感叹号和小游戏 UI，并通过鼠标控制让移动目标保持在捕获区域内。

> 当前仍是开发中的 MVP。C#/.NET 10/WinUI 3 软件已接入完整显示器捕获、四类双 ONNX 推理、状态机、CPU/DirectML、模型管理、受限自动调频和屏外感叹号兜底设置。当前 round3 `best.pt` 已固化为 `models/v0.1.0/` 的可发布模型，并导出为静态 FP32 ONNX；完整审核视频、现场仅观察、实时性能和自动钓鱼仍需人工验收，模型清单暂不允许自动输入。

## 从这里开始

- 最终用户：阅读 [使用手册](USER_GUIDE.md)。手册描述首个正式版本的安装和使用方式。
- 新开发者：先按 [开发环境部署](docs/development-setup.md) 安装自己负责部分的环境，再从 [开发文档索引](docs/README.md) 阅读设计约定。
- 准备录屏：阅读 [data_processing/README.md](data_processing/README.md)。
- 训练模型：阅读 [training/README.md](training/README.md)。
- 许可与再分发：阅读 [许可证与发布边界](docs/licensing.md)。

## 发布版安装

正式 Release 发布后，只需下载一个 `VRC-Fisher-Setup-x64.exe`。同一个安装向导中选择语言、安装目录、CPU-only 或 DirectML，以及是否立即下载模型。软件和全部运行数据位于所选安装目录；不要求用户预装 Python、.NET 或 CUDA。

## 快速使用

1. 启动 VRChat 并进入受支持的钓鱼世界。
2. 打开 VRC-Fisher，确认两个模型已安装并通过校验。
3. 选择显示 VRChat 的完整显示器和运行设备；识别频率默认保持“自动”。
4. 按目标世界设置“屏外感叹号兜底等待时间”，再运行“仅观察”确认状态识别正确。
5. 再明确启动“自动运行”；任何异常立即按 `F8` 停止。

当前尚未上传 GitHub Release；本地 `releases/app-v0.1.0/` 与 `releases/models-v0.1.0/` 是供维护者审核的发行物。完整说明和卸载行为见 [使用手册](USER_GUIDE.md)。

## ONNX 性能快照

当前开发机为 Ryzen 7 7840H + RTX 4060 Laptop GPU。正式 C# 检测器在 `2560 x 1600` 测试帧上的平均单帧耗时如下；locator 输入为 `960 x 960`，缓存小游戏裁剪输入为 `640 x 640`。

| 运行模式 | locator | 缓存 minigame | locator + minigame |
|---|---:|---:|---:|
| CPU-only / CPU | 58.80 ms | 23.68 ms | 108.81 ms |
| DirectML 包 / CPU | 68.81 ms | 28.40 ms | 103.54 ms |
| DirectML / GPU | 11.11 ms | 5.42 ms | 16.17 ms |
| DirectML / Auto | 10.99 ms | 4.95 ms | 15.83 ms |

```mermaid
xychart-beta
    title "C# locator 平均单帧耗时（越低越好）"
    x-axis ["CPU-only", "DML包-CPU", "DML-GPU", "DML-Auto"]
    y-axis "毫秒" 0 --> 80
    bar [58.80, 68.81, 11.11, 10.99]
```

本机 `Auto` 实际选择 DirectML GPU。保持 FP32 模型、`960/640` 输入和原阈值的 C# 优化后，CPU-only 的 locator、双模型和缓存 minigame P95 分别为 `65.21/119.79/24.79 ms`。运行时会分别统计三类负载并在固定边界内调节四个间隔；本机 CPU 初始值为 `100/150/40/500 ms`，DirectML 初始值为 `80/80/33/250 ms`。统计不增加模型调用。完整算法、开销和测量边界见 [性能与存储预算](docs/performance-budget.md) 与 [ONNX Runtime 单帧性能实测](docs/onnx-runtime-benchmark.md)。

## 开发者快速验证

三套工程彼此隔离，不要求一次安装全部工具：

```powershell
# C# 软件
Set-Location app
dotnet restore VrcFisher.sln
dotnet test VrcFisher.sln -c Debug

# 数据处理（Python 3.11 + uv）
Set-Location ..\data_processing
uv sync --locked --extra dev
uv run --offline pytest -q

# NVIDIA CUDA 训练工具（Python 3.11 + uv）
Set-Location ..\training
uv sync --locked --extra dev
uv run --offline pytest -q
uv run --offline vrc-preflight --task all
```

最后一条只检查数据，不启动训练。训练环境使用项目内 PyTorch CUDA wheel，不需要 Miniconda 或全局 CUDA Toolkit；完整前置条件、磁盘占用和故障检查见 [开发环境部署](docs/development-setup.md)。

## 仓库目录

| 目录 | 内容 |
|---|---|
| `app/` | 正式 C# 软件工程与自动化测试 |
| `data_processing/` | 录屏抽帧、全屏标注审计和双数据集生成 |
| `training/` | PyTorch/Ultralytics 训练与 ONNX 导出 |
| `models/` | 随源码仓库公开的已验收 `.pt`、ONNX、模型卡、许可证和校验清单 |
| `packaging/` | C# self-contained publish 与单一 Inno Setup 构建脚本 |
| `releases/` | 本地发布产物，不提交 Git |
| `docs/` | 全部开发者设计文档 |

正式产品从第一版开始使用 C#、.NET 10、WinUI 3 和 ONNX Runtime。Python 与 CUDA 只用于离线数据处理和开发机训练，不进入用户安装包。简体中文与 English 资源来自仓库中的 `.resw`，构建为 `VrcFisher.pri` 后随本体安装，不从 GitHub 单独下载语言包。每个已验收模型版本的 `.pt` 和 ONNX 都提交到 `models/vX.Y.Z/`，与源代码一起公开；GitHub Releases 只是软件按需下载经过校验的 ONNX 的渠道。用户端当前只发布 CPU-only 与 DirectML，不发布 CUDA 组件。

## 许可证

VRC-Fisher 是多许可证项目：原创应用、数据处理和通用工具代码采用 [MIT License](LICENSE)；完整 `training/` 子项目及通过 Ultralytics YOLO11 训练、导出的官方模型按上游标注的 AGPL-3.0 发布；其他组件遵循各自许可证。AGPL 允许商业使用，但分发完整组合或通过网络提供相应服务时必须履行对应源码义务。

录屏、抽帧、标注、审核图和生成数据集默认保持私有，不在 MIT 授权范围内。项目不包含 `vrc-auto-fish` 的代码、模型、图片、标注或文档，只独立实现通用技术思路。完整依赖清单和再分发规则见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 与 [许可证文档](docs/licensing.md)。开源许可证也不代表 VRChat 或目标世界允许自动化操作，使用者仍需遵守相应规则。

## 当前阻塞

`models/v0.1.0/` 已包含当前最好的 round3 `.pt` 和 ONNX，并由本地模型发布脚本生成对应清单。GitHub Release 仍需维护者审核本地发行物后再创建；即使发布模型，真实 VRChat 的“仅观察”、CPU/DirectML 实时性能和输入安全验收仍是独立门槛，当前清单不会自动启用输入。
