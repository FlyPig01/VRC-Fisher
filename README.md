# VRC-Fisher

VRC-Fisher 是一个仅面向 Windows 的 VRChat 钓鱼自动化项目。程序从用户选择的完整显示器捕获画面，以两个视觉模型定位钓鱼提示和小游戏 UI，再通过状态机控制鼠标。

> 当前仍是开发中的 MVP。C#/.NET 10/WinUI 3 分层工程、状态机、模型目录、Provider 入口、帧缓冲和 YOLO 后处理已建立并可测试；正式标注数据、可用 ONNX、真实 Windows Graphics Capture 适配和 Setup 仍未完成。

## 从这里开始

- 最终用户：阅读 [使用手册](USER_GUIDE.md)。手册描述首个正式版本的安装和使用方式。
- 开发者：从 [开发文档索引](docs/README.md) 开始，技术决策、架构、性能和发布约定均以 `docs/` 为准。
- 准备录屏：阅读 [data_processing/README.md](data_processing/README.md)。
- 训练模型：阅读 [training/README.md](training/README.md)。

## 发布版安装

正式 Release 发布后，只需下载一个 `VRC-Fisher-Setup-x64.exe`。同一个安装向导中选择语言、安装目录、CPU-only 或 DirectML，以及是否立即下载模型。软件和全部运行数据位于所选安装目录；不要求用户预装 Python、.NET 或 CUDA。

## 快速使用

1. 启动 VRChat 并进入受支持的钓鱼世界。
2. 打开 VRC-Fisher，确认两个模型已安装并通过校验。
3. 选择显示 VRChat 的完整显示器和运行设备。
4. 先运行“仅观察”，确认状态识别正确。
5. 再明确启动“自动运行”；任何异常立即按 `F8` 停止。

当前尚无正式 Release，上述步骤暂不能执行。完整说明和卸载行为见 [使用手册](USER_GUIDE.md)。

## 仓库目录

| 目录 | 内容 |
|---|---|
| `app/` | 正式 C# 软件工程；旧 Python 原型仅作移植参考 |
| `data_processing/` | 录屏抽帧、全屏标注审计和双数据集生成 |
| `training/` | PyTorch/Ultralytics 训练与 ONNX 导出 |
| `packaging/` | 目标为 C# self-contained publish 与单一 Inno Setup；脚本仍待重写 |
| `releases/` | 本地发布产物，不提交 Git |
| `docs/` | 全部开发者设计文档 |

正式产品从第一版开始使用 C#、.NET 10、WinUI 3 和 ONNX Runtime。Python 只用于离线数据处理与训练，不进入用户安装包。简体中文与 English 资源随 WinUI 软件本体内置，不从 GitHub 单独下载语言包；GitHub Releases 只提供经过校验的 ONNX 模型。CUDA 当前不在范围内。

## 当前阻塞

现有录屏已经抽帧，但还没有经过人工审核的标注。只有一段录屏也无法形成可信的训练集和验证集，因此当前不能训练、评估或发布正式模型。数据不足或标注审计失败时，流程必须停止，不允许用空数据或相邻帧随机拆分制造结果。
