# 技术栈与开发环境

## 1. 三套独立环境

所有源码放在同一个仓库，但职责和依赖必须隔离：

| 环境 | 目录 | 用途 | 是否发给用户 |
|---|---|---|---|
| 正式软件 | `app/` | 捕获、ONNX 推理、状态机、输入和 GUI | 是，编译为 C# 自包含程序 |
| 数据处理 | `data_processing/` | 录屏解码、抽帧、标签审计、数据集生成 | 否 |
| 模型训练 | `training/` | 训练 YOLO11n、验证和导出 ONNX | 否 |

正式软件没有 Python、PyTorch、Ultralytics、Miniconda 或 CUDA 依赖。开发机上的 Python 环境也不应与 C# 应用构建目录混用。

许可证按目录和制品分层：VRC-Fisher 原创应用、数据处理和通用工具代码采用 MIT；完整训练子项目、Ultralytics 基础权重及本项目由其产生的官方 `.pt/.onnx` 按上游标注的 AGPL-3.0 发布；第三方组件保持各自许可证。AGPL 允许商业使用，但不能把完整组合重新标为纯 MIT。详细边界见 [许可证与发布边界](licensing.md)。

## 2. 正式软件

| 部分 | 技术 | 选择原因 |
|---|---|---|
| 语言与平台 | C#、.NET 10、Windows x64 | 原生 Windows 集成明确，运行和打包不携带 Python 环境 |
| GUI | WinUI 3、Windows App SDK | 现代 Fluent 控件、高 DPI、主题和 Windows 11 视觉 |
| 界面架构 | 页面 + 最小能力接口 | 页面只编排交互，不获得捕获、推理或输入实现 |
| 屏幕捕获 | Windows Graphics Capture | 按窗口句柄持续捕获，仅接受 `VRChat.exe` 主窗口 |
| 推理 | Microsoft.ML.OnnxRuntime | 同一 ONNX 可切换 CPU 与 DirectML Provider |
| 输入和窗口 | Win32 API / P/Invoke | 获取 VRChat 窗口、局内热键状态和鼠标控制 |
| 配置 | System.Text.Json | 在安装目录内存储可审计的 JSON |
| 日志 | Microsoft.Extensions.Logging | 统一结构化日志接口，输出到安装目录 |
| 安装 | Inno Setup 6 | 一个安装器中提供语言、目录、快捷方式和可选模型下载 |

正式基线不采用 WPF、Avalonia、Electron、MSIX、PyInstaller 或 OpenCV。项目只面向现代 Windows，不需要为跨平台引入额外运行时。

WinUI 3 的代价是体积与空闲内存高于精简 C++/Win32。项目接受该代价，以换取较低的 Windows UI 开发复杂度和一致的现代外观；资源上限和验证方式见 [性能与存储预算](performance-budget.md)。

### C# 与 C++ 的取舍

首版正式软件选择 C#，不是因为 C++ 做不到，而是因为本项目的主要风险在实时管线、模型管理、状态机、安装器和可观察性，而不是极限的原生 UI 性能。

| 维度 | C# / WinUI 3 | C++ / Win32 或 WinUI 3 |
|---|---|---|
| Windows Graphics Capture、Win32、DirectML 集成 | 有成熟 .NET 绑定和 P/Invoke 路径 | 可直接调用原生 API |
| GUI 开发与迭代 | 原生控件、资源本地化和内存安全代码更快 | 生命周期、ABI 和资源管理成本更高 |
| 安装体积与内存 | 通常更大 | 有机会更小 |
| 运行时上限 | 对 30 FPS 小模型目标足够，但必须实测 | 更适合以后证明需要极限优化时使用 |
| 维护风险 | 依赖托管运行时和 Windows App SDK 版本 | 需要自行承担更多内存、线程和 DLL 管理风险 |

在正式模型和回放数据出现前，直接用 C++ 优化是没有证据支持的。C# 版本如果经过实测仍无法满足帧龄、CPU 或内存门槛，再针对热点迁移单个基础设施模块，而不是预先把整个项目改成 C++。

## 3. 运行时设备

Setup 只发布一套 `Microsoft.ML.OnnxRuntime.DirectML 1.24.4` 自包含程序。该包同时具有 CPU 和 DirectML 执行后端：

| 软件选项 | 实际行为 | 硬件范围 |
|---|---|---|
| `Auto` | 优先 DirectML，初始化失败时回退 CPU 并记录原因 | Windows 10/11 x64 |
| `GPU` | 强制 DirectML，失败即报错 | 支持 DirectML 的 NVIDIA、AMD、Intel GPU |
| `CPU` | 强制 CPU 后端 | 兼容 x64 CPU |

独立 `Microsoft.ML.OnnxRuntime` CPU-only 构建已取消。这样安装器、许可证、卸载和测试都只有一套程序目录；CPU 用户仍可在软件内直接选择 `CPU`，不需要重新安装。

运行快照结构化记录用户选择、实际后端、实际设备、是否回退和原因；内部 Provider 字符串只用于日志与历史基准。三种设备选项读取相同的 `locator.onnx` 与 `minigame.onnx`，切换设备不需要重新训练、转换或下载模型。

运行时默认启用受限自动调频。它对实际发生的三类检测分别记录 P95，不增加 ONNX 调用；CPU 与 DirectML 使用相同算法但不同首轮初值。画像按实际后端、硬件、模型版本和捕获分辨率隔离，并只写入用户选择的安装目录。

正式模型使用两个静态输入契约：`locator.onnx` 为 `960 x 960`，`minigame.onnx` 为 `640 x 640`。C# 分别读取并校验两个 ONNX 的输入元数据。以后增加 CUDA Provider 时仍可使用同一组 ONNX，无需重新训练；FP16、INT8 或 TensorRT 等专项产物需要单独转换和验证。

DirectML 复用 Windows 的 DirectML/DirectX 系统组件，而 CUDA Provider 需要携带或依赖 NVIDIA 专用运行库。项目不发布 CUDA 组件，因此用户硬件不局限于 NVIDIA；这也不表示任何模型、显卡或分辨率下 GPU 都比 CPU 快。

## 4. 离线 Python 环境

| 环境 | 固定依赖 | 说明 |
|---|---|---|
| 数据处理 | Python 3.11、uv、PyAV、Pillow、NumPy | CPU 足够，不需要 CUDA |
| 训练 | Python 3.11、uv、PyTorch `2.13.0+cu130`、Ultralytics `8.4.118`、ONNX | 当前使用 NVIDIA CUDA；依赖锁定在 `training/uv.lock` |

训练保存 `.pt` 检查点和实验结果，只有审核后的模型才导出为 ONNX。`.pt` 便于继续训练，但要求 Python/PyTorch 运行环境，不适合作为用户端格式。

CUDA 只影响开发机训练速度，不影响最终用户选择 CPU 或 DirectML，也不改变模型类别和 ONNX 契约。当前训练项目直接安装带 CUDA 13.0 运行库的 PyTorch Windows wheel，不依赖 Miniconda、`nvcc`、`CUDA_PATH` 或全局 CUDA Toolkit。其他开发机必须先用 `nvidia-smi` 确认 NVIDIA 驱动可用，再按已提交的 `uv.lock` 建立项目内环境；完整命令见 [开发环境部署](development-setup.md)。

## 5. 当前开发机

截至 2026-08-14，本机检查结果：

| 项目 | 当前状态 |
|---|---|
| 系统 | Windows x64 |
| .NET SDK | 10.0.301 |
| Python | 3.11 可用 |
| uv | 0.11.0 |
| GPU | NVIDIA GeForce RTX 4060 Laptop GPU，8 GB 显存 |
| Inno Setup `ISCC.exe` | 已安装 6.7.3；用户级路径 `C:\Users\32615\AppData\Local\Programs\Inno Setup 6\ISCC.exe` |
| NVIDIA 驱动 | 610.88，驱动可识别 GPU；系统未安装全局 CUDA Toolkit |
| CUDA 训练环境 | `training/.venv`；PyTorch `2.13.0+cu130`、CUDA Runtime 13.0、Ultralytics 8.4.118 |
| CUDA 验证 | `torch.cuda.is_available() == True`；2048 方阵乘法与 YOLO11n 无权重前向已在 `cuda:0` 成功 |
| 项目内训练缓存 | `training/.uv-cache`；南京大学 PyTorch 镜像，版本与哈希由 `uv.lock` 固定 |

这张表只描述当前开发机，不是用户前置条件。C# 正式软件使用 self-contained 发布，用户不需要预装 .NET SDK、Python 或 CUDA。

训练环境目前只支持仓库锁定的 Windows/NVIDIA CUDA 基线；这不表示正式软件只适配 NVIDIA。正式用户端的 DirectML 可使用兼容的 NVIDIA、AMD 或 Intel GPU，软件内的 CPU 选项不需要 GPU。

## 6. 版本策略

C# 解决方案必须锁定 Windows App SDK 与 ONNX Runtime DirectML 包的精确版本，并提交锁定结果。训练环境同样应在 `training/` 内固定版本。

新开发者不应照搬当前开发机的 Python、Inno Setup 或仓库绝对路径。部署文档使用 `py -3.11` 发现本机解释器；若机器没有 Python Launcher，可以把命令中的解释器变量替换为本机 Python 3.11 的实际路径。

任何依赖升级都必须重新执行离线回放、Provider 检查、性能基准和现场观察测试；不能只因出现新版本就升级。
