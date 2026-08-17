# 开发环境

本文只定义各工作区的开发依赖、环境隔离和验证入口。具体命令由对应目录的 README 维护。

## 1. 环境划分

| 工作区 | 必需环境 | 本地环境或输出 | 操作入口 |
|---|---|---|---|
| C# 软件 | Windows x64、Git、.NET 10 SDK | `app/**/bin/`、`app/**/obj/` | [`app/README.md`](../app/README.md) |
| 数据处理 | Python 3.11、uv | `data_processing/.venv/` | [`data_processing/README.md`](../data_processing/README.md) |
| 模型训练 | Python 3.11、uv、NVIDIA GPU 与兼容驱动 | `training/.venv/`、训练缓存与 `runs/` | [`training/README.md`](../training/README.md) |
| 发布打包 | .NET 10 SDK、Inno Setup 6 | `build/`、`releases/` | [`packaging/README.md`](../packaging/README.md) |

各环境相互独立：开发软件不需要 Python；处理数据不需要 .NET 或 GPU；只有训练依赖 PyTorch CUDA 环境。锁定的 PyTorch wheel 自带所需 CUDA 运行库，不要求 Miniconda、全局 CUDA Toolkit、`nvcc` 或 `CUDA_PATH`。

## 2. 验证边界

- 软件工作区负责还原、构建和 Debug/Release 测试。
- 数据处理工作区负责工具测试、标注审计和数据集生成。
- 训练工作区负责训练预检、训练、评估和 ONNX 导出；预检不会自动开始训练。
- 打包工作区负责 self-contained 发布、Setup 编译和发布物完整性检查。

只执行正在修改的工作区命令；跨模块发布前再完成全部相关验证。

## 3. 本地文件

以下内容只保存在仓库内并由 `.gitignore` 排除：

- `.venv/`、uv/NuGet/Ultralytics 缓存；
- 录屏、抽帧、标注、数据集；
- 训练 `runs/`、临时导出和审核视频；
- `build/`、`releases/` 与现场日志；
- 未验收模型和开发用模型副本。

提交前至少执行相关目录测试、`git diff --check`，并确认没有把上述本地文件加入 Git。
