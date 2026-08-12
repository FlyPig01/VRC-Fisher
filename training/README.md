# Training

此目录使用 Python、PyTorch 和 Ultralytics YOLO11n 训练两个检测模型，并导出 C# 软件使用的 ONNX。训练环境、`.pt`、数据集和运行记录不进入用户安装包。

## 准备环境

需要 Python 3.11 和 uv。`pyproject.toml` 声明 CPU 基线依赖的兼容上限；uv lock 文件应随依赖变化更新。CUDA 版 PyTorch 不在仓库依赖中自动选择，需按 NVIDIA 驱动和 PyTorch 官方矩阵单独替换安装。

```powershell
Set-Location E:\MyTools\VRC-Fisher\training
uv venv --python 3.11 .venv
uv pip install --python .venv\Scripts\python.exe -e .
uv pip install --python .venv\Scripts\python.exe pytest
.venv\Scripts\python.exe -m pytest -q
```

当前默认用 CPU 跑通数据、训练和导出流程。CPU 训练可能很慢；确认数据有效且耗时不可接受后，再根据当时的 NVIDIA 驱动与 PyTorch 兼容矩阵决定 CUDA 环境。是否使用 CUDA 训练不影响用户端 CPU-only 或 DirectML。

## 输入

数据由 `data_processing/` 按录屏划分后写入：

```text
datasets/locator/data.yaml
datasets/locator/images/{train,val,test}/
datasets/locator/labels/{train,val,test}/
datasets/minigame/data.yaml
datasets/minigame/images/{train,val,test}/
datasets/minigame/labels/{train,val,test}/
```

没有非空且经过审计的训练集与验证集时停止。只有一段录屏不能提供可信验证结果。

## 训练

训练参数位于 `configs/default.toml`，默认基模型是 `yolo11n.pt`：

```powershell
.venv\Scripts\vrc-train.exe --task all
```

也可以单独训练：

```powershell
.venv\Scripts\vrc-train.exe --task locator
.venv\Scripts\vrc-train.exe --task minigame
```

Ultralytics 结果写入 `runs/locator*` 和 `runs/minigame*`。每次运行会生成新的目录；不要假定权重总在未带编号的路径中。

## 导出

审核两个任务实际产生的 `best.pt` 后，用明确路径导出：

```powershell
.venv\Scripts\vrc-export.exe `
  --locator runs\<locator-run>\weights\best.pt `
  --minigame runs\<minigame-run>\weights\best.pt
```

产物：

```text
exports/locator.onnx
exports/minigame.onnx
```

确认模型契约与独立录屏验证通过后，才复制到 C# 开发目录用于回放：

```powershell
Copy-Item exports\locator.onnx ..\app\models\locator.onnx
Copy-Item exports\minigame.onnx ..\app\models\minigame.onnx
```

`.pt` 用于开发时训练与实验，用户软件只加载两个 ONNX。CPU-only 与 DirectML 使用同一份 ONNX，不需要重新训练。

模型设计、数据停止条件和验收指标见 [视觉、数据与训练](../docs/vision-and-training.md)。
