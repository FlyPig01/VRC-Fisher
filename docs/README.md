# 开发文档

本目录是 VRC-Fisher 技术决策的唯一来源。根目录 `USER_GUIDE.md` 只说明用户如何安装和使用；各子目录 `README.md` 只说明该目录的实际状态和可执行命令。

## 阅读顺序

1. [开发环境部署](development-setup.md)：从空白 Windows 环境部署 C#、数据处理或 CUDA 训练环境。
2. [技术栈](technical-stack.md)：开发环境、运行环境、框架选择和版本策略。
3. [软件架构](software-architecture.md)：C# 分层、实时管线、状态机和 WinUI 3 前端。
4. [视觉与训练](vision-and-training.md)：双 YOLO11n、四类全屏标注、数据生成、训练和 ONNX 契约。
5. [性能与存储预算](performance-budget.md)：开发占用、发布体积、运行资源和验收方法。
6. [安装与发布](installation-and-release.md)：单一 Setup、组件选择、模型下载、升级和卸载。
7. [许可证与发布边界](licensing.md)：MIT 原创代码、Ultralytics AGPL、模型、数据集和第三方声明。

## 固定基线

| 范围 | 决定 |
|---|---|
| 正式软件 | C# / .NET 10 / WinUI 3 / Windows x64 |
| 捕获 | Windows Graphics Capture，完整显示器 |
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

当前已实现 C# 分层工程、双 ONNX Runtime 推理、YOLO 输出解码、旧版状态机、连续帧证据、前台 VRChat 输入保护、鼠标安全释放、F8 紧急停止、最新帧缓冲、模型清单/校验/成组事务下载、GitHub Release 查询、取消与有限重试、WinUI 模型管理、CPU/DirectML Provider、WinUI 系统捕获选择器、D3D11/WGC CPU readback、内置中英文资源和 18 项 C# 自动化测试。单一 Inno Setup 已实际生成，并分别完成 CPU-only 与 DirectML 安装和无模型启动验证。

四类视觉契约和新的计时兜底是当前文档基线。Python 数据工具和双数据集生成器已同步为四类，预标注直接使用项目 YOLO TXT，并由仅监听本机的浏览器标注器保存草稿、区分待审核与负样本；C# 仍映射旧八类结果，设置页也没有兜底时间滑块。训练环境已在 RTX 4060 上完成首轮训练，但 locator 感叹号独立验证样本过少，minigame 的 `moving_target` Recall 只有 0.392。首轮权重只用于人工复核的预标注，两个有效 ONNX、识别性能和真实 VRChat 场景验收均未完成。
