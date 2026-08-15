# 开发文档

本目录是 VRC-Fisher 技术决策的唯一来源。根目录 `USER_GUIDE.md` 只说明用户如何安装和使用；各子目录 `README.md` 只说明该目录的实际状态和可执行命令。

## 阅读顺序

1. [开发环境部署](development-setup.md)：从空白 Windows 环境部署 C#、数据处理或 CUDA 训练环境。
2. [技术栈](technical-stack.md)：开发环境、运行环境、框架选择和版本策略。
3. [软件架构](software-architecture.md)：C# 分层、实时管线、状态机和 WinUI 3 前端。
4. [视觉与训练](vision-and-training.md)：双 YOLO11n、四类全屏标注、数据生成、训练和 ONNX 契约。
5. [性能与存储预算](performance-budget.md)：开发占用、发布体积、运行资源和验收方法。
6. [ONNX Runtime 单帧性能实测](onnx-runtime-benchmark.md)：C# CPU/DirectML 与 Python CPU/CUDA 的平均值、P50/P95 和调度建议。
7. [安装与发布](installation-and-release.md)：单一 Setup、组件选择、模型下载、升级和卸载。
8. [许可证与发布边界](licensing.md)：MIT 原创代码、Ultralytics AGPL、模型、数据集和第三方声明。

## 固定基线

| 范围 | 决定 |
|---|---|
| 正式软件 | C# / .NET 10 / WinUI 3 / Windows x64 |
| 捕获 | Windows Graphics Capture，仅限 `VRChat.exe` 主窗口 |
| 运行时推理 | ONNX Runtime CPU-only 或 DirectML |
| 数据处理 | Python 3.11 / PyAV / Pillow |
| 训练 | Python 3.11 / PyTorch 2.13 CUDA 13.0 / Ultralytics YOLO11n；locator 960、minigame 640 |
| 安装 | 一个 Inno Setup 安装程序 |
| CUDA | 仅用于 NVIDIA 开发机训练；项目内 wheel，不安装 Miniconda 或全局 Toolkit |
| 许可 | 原创代码 MIT；完整训练子项目与 Ultralytics 衍生模型采用上游标注的 AGPL-3.0；第三方组件各自许可 |
| 模型开源 | 已验收 `.pt` 与 ONNX 随源码提交到 `models/vX.Y.Z/`；Release 仅作软件下载安装渠道 |

视觉类别固定为四类：`bite_indicator`、`minigame_panel`、`catch_zone`、`moving_target`。不训练成功、失败、轨道或进度条类别。感叹号不可见时只能由用户配置的兜底等待时间触发一次收钩尝试；小游戏面板连续消失后执行一次收杆点击。

以下决定不是候选方案：OpenCV 不参与运行时识别；WPF 不作为前端；Python 不作为正式软件运行环境；`.pt` 不进入最终用户安装目录但必须在源码仓库公开；CPU-only 与 DirectML 不需要分别训练模型。

## 事实与目标

文档中的状态使用以下含义：

- **已实现**：仓库中存在对应代码，并能按文档命令检查。
- **目标设计**：已经确定的正式实现要求，但代码尚未完成。
- **估算**：构建前的容量或性能预算，不能作为实测结论发布。

当前已实现 C# 分层工程、四类双 ONNX Runtime 推理、显式 Raw/NMS 输出解码、动画感叹号时序证据、默认禁用且为 `5-30` 秒的屏外感叹号兜底、固定启用的受限自动调频和安装目录性能画像、前台 VRChat 输入保护、鼠标安全释放、默认 F8 且可确认更改的全局启停热键、15 Hz 点击穿透调试覆盖层、最新帧缓冲、模型清单/校验/成组事务下载、GitHub Release 更新查询、WinUI 模型管理、CPU/DirectML Provider、仅限 VRChat 主窗口的 WGC 捕获、20 种内置语言和全局错误通知。单一 Inno Setup 已实际生成，并分别完成 CPU-only 与 DirectML 安装和无模型启动验证；20 种语言和覆盖层的完整实机安装验收仍待执行。

round3 的最佳权重已导出为 `training/exports/locator.onnx` 与 `minigame.onnx`，并固化到 `models/v0.1.0/`；开发副本位于 `app/models/`。导出契约、抽样 PT/ONNX 对比以及 C# CPU/DirectML 对真实全屏帧的加载推理均已通过。该版本的 `automatic_allowed` 已设为 `true` 以进行实机验证，但不代表所有场景精度或自动钓鱼成功率已经验收。
