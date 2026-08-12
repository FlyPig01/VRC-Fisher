# 开发文档

本目录是 VRC-Fisher 技术决策的唯一来源。根目录 `USER_GUIDE.md` 只说明用户如何安装和使用；各子目录 `README.md` 只说明该目录的实际状态和可执行命令。

## 阅读顺序

1. [技术栈](technical-stack.md)：开发环境、运行环境、框架选择和版本策略。
2. [软件架构](software-architecture.md)：C# 分层、实时管线、状态机和 WinUI 3 前端。
3. [视觉与训练](vision-and-training.md)：双 YOLO11n、全屏标注、数据生成、训练和 ONNX 契约。
4. [性能与存储预算](performance-budget.md)：开发占用、发布体积、运行资源和验收方法。
5. [安装与发布](installation-and-release.md)：单一 Setup、组件选择、模型下载、升级和卸载。

## 固定基线

| 范围 | 决定 |
|---|---|
| 正式软件 | C# / .NET 10 / WinUI 3 / Windows x64 |
| 捕获 | Windows Graphics Capture，完整显示器 |
| 运行时推理 | ONNX Runtime CPU-only 或 DirectML |
| 数据处理 | Python 3.11 / PyAV / Pillow |
| 训练 | Python 3.11 / PyTorch / Ultralytics YOLO11n |
| 安装 | 一个 Inno Setup 安装程序 |
| CUDA | 当前不采用 |

以下决定不是候选方案：OpenCV 不参与运行时识别；WPF 不作为前端；Python 不作为正式软件运行环境；`.pt` 不发给用户；CPU-only 与 DirectML 不需要分别训练模型。

## 事实与目标

文档中的状态使用以下含义：

- **已实现**：仓库中存在对应代码，并能按文档命令检查。
- **目标设计**：已经确定的正式实现要求，但代码尚未完成。
- **估算**：构建前的容量或性能预算，不能作为实测结论发布。

当前已实现 C# 分层工程、状态机、连续帧证据、鼠标安全释放、F8 紧急停止、最新帧缓冲、模型清单/校验/事务下载基础、CPU Provider 入口、YOLO 常见输出解码和 7 项自动化测试。正式标注数据、两个有效 ONNX、真实 Windows Graphics Capture 适配、完整模型下载 GUI 和 C# 打包链仍未完成。当前自动模式必须在正式模型契约与现场验证完成后才可发布；没有人工标注时不能训练或报告准确率。`app/` 中的旧 Python 原型与 `packaging/` 中的旧 PyInstaller 脚本只能用于逻辑参考，不代表正式运行时。
