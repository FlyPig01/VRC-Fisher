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

当前已实现 C# 分层工程、双 ONNX Runtime 推理、YOLO 输出解码、状态机、连续帧证据、前台 VRChat 输入保护、鼠标安全释放、F8 紧急停止、最新帧缓冲、模型清单/校验/成组事务下载、GitHub Release 查询、取消与有限重试、WinUI 模型管理、CPU/DirectML Provider、WinUI 系统捕获选择器、D3D11/WGC CPU readback、内置中英文资源和 18 项 C# 自动化测试。单一 Inno Setup 已实际生成，并分别完成 CPU-only 与 DirectML 安装和无模型启动验证。正式标注数据、两个有效 ONNX、识别性能和真实 VRChat 场景验收仍未完成；没有人工标注时训练入口必须停止，不能报告准确率或自动钓鱼成功率。
