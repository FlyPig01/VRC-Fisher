# VRC-Fisher

VRC-Fisher 是一个仅面向 Windows 的 VRChat 钓鱼自动化项目。程序捕获用户选择的完整显示器，识别咬钩感叹号和小游戏 UI，并通过鼠标控制让移动目标保持在捕获区域内。

> 当前仍是开发中的 MVP。C#/.NET 10/WinUI 3 软件、Windows Graphics Capture、双 ONNX 推理基础设施、模型管理、CPU/DirectML 运行组件和单一 Inno Setup 已实现并通过本机构建、安装与启动验证。Python 数据工具已同步为四类，并支持使用首轮 `best.pt` 生成原生 YOLO 预标注，再通过项目内本地浏览器标注器审核；C# 状态机与推理类别映射仍需从旧八类契约同步。首轮模型因数据覆盖不足尚未验收，可用 ONNX 和真实 VRChat 自动钓鱼验收仍未完成。

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
3. 选择显示 VRChat 的完整显示器和运行设备。
4. 按目标世界设置“屏外感叹号兜底等待时间”，再运行“仅观察”确认状态识别正确。
5. 再明确启动“自动运行”；任何异常立即按 `F8` 停止。

当前尚无已上传到 GitHub 的正式 Release；本地构建产物只用于开发验收。完整说明和卸载行为见 [使用手册](USER_GUIDE.md)。

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

当前三段录屏已完成首轮训练，但数据覆盖不合格：locator 独立验证集只有 6 个 `bite_indicator`，minigame 的 `moving_target` Recall 只有 0.392，且训练/验证目标外观存在明显域偏移。首轮权重只用于生成需要人工复核的预标注；补充多外观、多录屏数据并重新训练前不得导出发布模型。
