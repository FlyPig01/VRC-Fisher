# 开发环境部署

本文是新开发者从空白 Windows 环境部署仓库的唯一入口。仓库包含三套相互隔离的工程；只需要开发其中一部分时，不必安装另外两部分的工具。

## 1. 选择工作范围

| 工作范围 | 需要安装 | 不需要安装 |
|---|---|---|
| C# 软件 `app/` | Git、.NET 10 SDK | Python、PyTorch、CUDA Toolkit、Miniconda |
| 数据处理 `data_processing/` | Git、Python 3.11、uv | .NET、GPU、CUDA |
| 模型训练 `training/` | Git、Python 3.11、uv、受支持的 NVIDIA GPU 与驱动 | Miniconda、全局 CUDA Toolkit、.NET |
| 安装包 `packaging/` | C# 软件要求、Inno Setup 6 | Python、训练环境、模型文件 |

正式用户安装包与开发环境不同。用户端是 C# 自包含程序，只使用 ONNX Runtime CPU-only 或 DirectML，不要求 Python、Miniconda、PyTorch 或 CUDA。

## 2. 安装前置工具

Windows 11 自带 `winget`。只安装自己负责范围需要的工具，不必执行全部命令：

```powershell
# 所有人
winget install --exact --id Git.Git

# C# 软件开发
winget install --exact --id Microsoft.DotNet.SDK.10

# 数据处理或模型训练
winget install --exact --id Python.Python.3.11
winget install --exact --id astral-sh.uv

# 仅构建正式 Setup
winget install --exact --id JRSoftware.InnoSetup
```

安装后重新打开 PowerShell，再验证需要的命令：

```powershell
git --version
dotnet --version
py -3.11 --version
uv --version
```

只做 Python 工作时，`dotnet` 不存在是正常的；只做 C# 工作时，`py` 和 `uv` 不存在也是正常的。NVIDIA 训练机还需通过 Windows Update、笔记本厂商或 NVIDIA 安装显卡驱动，项目不自动修改驱动。

## 3. 获取仓库

```powershell
git clone <repository-url> VRC-Fisher
Set-Location VRC-Fisher
git status --short
```

将 `<repository-url>` 替换为仓库实际 Git URL；当前本地仓库尚未配置公开远端，因此文档不虚构地址。下文默认当前目录是仓库根目录。

不要把虚拟环境、录屏、数据集、训练运行目录或未验收模型提交到 Git。唯一例外是完成验收和许可证检查后，由发布脚本生成的 `models/vX.Y.Z/`；其中两个 `.pt` 和两个 ONNX 必须随源码仓库提交。

## 4. C# 软件

先确认 SDK 主版本：

```powershell
dotnet --version
```

输出必须是 `10.x`。随后执行：

```powershell
Set-Location app
dotnet restore VrcFisher.sln
dotnet build VrcFisher.sln -c Debug
dotnet test VrcFisher.sln -c Debug
```

没有 `app/models/locator.onnx` 和 `app/models/minigame.onnx` 时，GUI 仍应能够构建和启动，但识别与自动输入必须保持禁用。不要使用未经审核的模型绕过这个限制。

构建安装包还需安装 Inno Setup 6，之后回到仓库根目录阅读 [packaging/README.md](../packaging/README.md)。

## 5. 数据处理

必须使用 Python 3.11，不支持 3.12 或其他主次版本。先让 Windows Python Launcher 找到解释器：

```powershell
$Python311 = py -3.11 -c "import sys; print(sys.executable)"
& $Python311 --version
uv --version
```

从仓库根目录建立目录内环境并验证：

```powershell
Set-Location data_processing
uv sync --locked --python $Python311 --extra dev
uv run --offline pytest -q
```

依赖安装在 `data_processing/.venv/`。录屏、抽帧、标注和生成数据也只在仓库的对应忽略目录中工作；完整处理流程见 [data_processing/README.md](../data_processing/README.md)。

## 6. NVIDIA CUDA 训练

当前可复现的训练基线是 Windows x64、Python 3.11、PyTorch `2.13.0+cu130` 和 Ultralytics `8.4.118`。PyTorch wheel 已包含训练所需的 CUDA 运行库，因此：

- 不安装 Miniconda；
- 不安装全局 CUDA Toolkit；
- 不需要 `nvcc` 或 `CUDA_PATH`；
- 必须有 NVIDIA GPU，并让 `nvidia-smi` 正常识别显卡和驱动。

先检查驱动：

```powershell
nvidia-smi
```

回到仓库根目录，再建立项目内训练环境：

```powershell
$Python311 = py -3.11 -c "import sys; print(sys.executable)"
Set-Location training
uv sync --locked --python $Python311 --extra dev
```

`training/pyproject.toml` 将环境固定在 `training/.venv/`，将 uv 缓存固定在 `training/.uv-cache/`，并从南京大学 PyTorch 镜像取得锁定的 CUDA wheel。`uv.lock` 记录具体版本、来源和哈希；不要用手工 `pip install` 覆盖锁定环境。

下载完成后执行离线验证：

```powershell
uv sync --offline --locked --extra dev
uv run --offline pytest -q
uv run --offline python -c "import torch; print(torch.__version__, torch.version.cuda); print(torch.cuda.is_available()); print(torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'NO CUDA GPU')"
uv run --offline vrc-preflight --task all
```

期望结果包括：

```text
torch 2.13.0+cu130
CUDA runtime 13.0
torch.cuda.is_available() == True
数据预检最后输出 READY，并明确 training was not started
```

如果 `torch.cuda.is_available()` 为 `False`，停止训练配置工作，先检查 NVIDIA 驱动、实际解释器和 `.venv` 中的 PyTorch 版本。不要通过安装完整 CUDA Toolkit 进行猜测性修复；本项目不使用它。

## 7. 训练审批门

部署成功不代表数据适合训练。当前训练入口要求人工提供 `--confirm-reviewed`，并会再次运行数据预检；但结构预检只能证明文件和标签格式合法，不能证明样本数量、独立性或覆盖范围足够。

正式训练命令是：

```powershell
uv run vrc-train --task all --confirm-reviewed
```

在维护者审核数据和参数并明确允许前，不得执行该命令。新的干净环境第一次训练时会在 `training/` 下下载 `yolo11n.pt`；该缓存基础权重不是项目发布模型，不提交到仓库。

## 8. 本地目录与存储

当前 CUDA 环境的逻辑文件大小约为：

| 路径 | 当前逻辑大小 | 可否删除 |
|---|---:|---|
| `training/.venv/` | 3.28 GiB | 可；之后用 `uv sync` 重建 |
| `training/.uv-cache/` | 5.40 GiB | 可；删除后离线重建不可用，需要重新下载 |
| `data_processing/.venv/` | 0.15 GiB | 可；之后用 `uv sync` 重建 |

`.venv` 与 `.uv-cache` 可能通过硬链接共享磁盘块，实际占用不能直接把两行相加。删除缓存不会卸载环境；删除环境也不会删除数据集。训练结果写入 `training/runs/`，临时导出写入 `training/exports/`，两者均不提交 Git。验收通过后由模型发布脚本把选定的两个 `best.pt` 和两个 ONNX 固化到 `models/vX.Y.Z/`，该目录必须提交。

## 9. 常用验证

在提交改动前，按修改范围运行：

```powershell
# C#
Set-Location app
dotnet test VrcFisher.sln -c Debug

# 数据处理
Set-Location ..\data_processing
uv run --offline pytest -q

# 训练工具与数据结构，不启动训练
Set-Location ..\training
uv run --offline pytest -q
uv run --offline vrc-preflight --task all
```

模型训练、完整视频推理和安装包构建耗时与副作用更大，应按各目录 README 的审批和输入要求单独执行。
