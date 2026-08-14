# Training

此目录使用 Python、PyTorch 和 Ultralytics YOLO11n 训练两个检测模型，并导出 C# 软件使用的 ONNX。训练环境、`.pt`、数据集和运行记录不进入用户安装包。

## 许可证

完整 `training/` 子项目按上游标注的 [AGPL-3.0](LICENSE) 发布。Ultralytics 本身、`yolo11n.pt`、本项目训练产生的官方 `.pt` 以及由其导出的官方 `.onnx` 不属于根目录 MIT License；项目随发布物提供上游完整许可证正文，不自行扩张或缩减其版本选择。AGPL 允许商业和收费使用，但分发或通过网络提供覆盖作品时必须履行对应源码等义务。

训练数据默认私有，不因 AGPL 或根目录 MIT 自动公开或获得再授权。模型发布前必须根据 [MODEL_CARD.template.md](MODEL_CARD.template.md) 生成无 `TBD` 的模型卡，并阅读 [许可证边界](../docs/licensing.md)。验收通过的两个 `.pt` 与两个 ONNX 必须一起固化到源码仓库的 `models/vX.Y.Z/`；Release 只是给软件下载安装 ONNX 的渠道。本项目不使用 `vrc-auto-fish` 的代码、权重、截图或标注。

> 项目视觉基线为四类：locator 使用 `bite_indicator`、`minigame_panel`，minigame 使用 `catch_zone`、`moving_target`。数据生成和 C# 推理映射均已同步；标注审计失败时不得开始训练，完整视频与现场验收失败时不得发布模型。

## 准备环境

需要 Windows x64、Python 3.11、uv，以及能被 `nvidia-smi` 识别的 NVIDIA GPU。当前锁定 PyTorch `2.13.0+cu130` 和 Ultralytics `8.4.118`。PyTorch wheel 自带所需 CUDA 13.0 运行库，不需要 Miniconda、全局 CUDA Toolkit、`nvcc` 或 `CUDA_PATH`。

```powershell
nvidia-smi
$Python311 = py -3.11 -c "import sys; print(sys.executable)"
$Repo = "C:\path\to\VRC-Fisher"
Set-Location (Join-Path $Repo "training")
uv sync --locked --python $Python311 --extra dev
```

环境与下载缓存都保存在仓库内：

```text
training/.venv/
training/.uv-cache/
```

PyTorch wheel 从 `pyproject.toml` 固定的南京大学镜像下载，具体版本、来源和哈希由 `uv.lock` 锁定。不要用手工 `pip install` 覆盖环境。首次同步完成后执行离线验证：

```powershell
uv sync --offline --locked --extra dev
uv run --offline pytest -q
uv run --offline python -c "import torch; print(torch.__version__, torch.version.cuda); print(torch.cuda.is_available()); print(torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'NO CUDA GPU')"
uv run --offline vrc-preflight --task all
```

本机已确认输出 PyTorch `2.13.0+cu130`、CUDA Runtime `13.0`、`torch.cuda.is_available() == True` 和 `NVIDIA GeForce RTX 4060 Laptop GPU`。其他开发者只需满足驱动与锁定 wheel 的兼容条件，不需要复制本机 Python 绝对路径。完整从零部署和故障处理见 [开发环境部署](../docs/development-setup.md)。

是否使用 CUDA 训练不影响用户端 CPU-only 或 DirectML。正式用户软件不包含这套 Python 环境。

Ultralytics 的本地设置固定写入 `training/.ultralytics/`；训练运行、权重和导出也都留在本目录。训练工具不会使用用户目录中的全局 Ultralytics 设置。

## 输入

数据由 `data_processing/` 按录屏划分后写入：

```text
datasets/locator/data.yaml
datasets/locator/images/{train,val}/
datasets/locator/labels/{train,val}/
datasets/locator/split.json
datasets/minigame/data.yaml
datasets/minigame/images/{train,val}/
datasets/minigame/labels/{train,val}/
datasets/minigame/split.json
```

没有非空且经过四类契约审计的训练集与验证集时停止。只有一段录屏不能提供可信验证结果。

完整视频测试不进入上述 YOLO 数据集。未标注的完整大屏视频放入 `training/test/videos/`，双模型生成 `training/test/results/` 中的带框视频供人工审核；不能据此计算 mAP、Precision、Recall。

## 训练前审核

先在 `data_processing/output/review/` 人工检查生成图，再核对两个 `split.json`。确认后运行只读预检：

```powershell
uv run vrc-preflight --task all
```

预检检查类别契约、图片和 TXT 一一对应、YOLO 坐标、每个 train/val 集合都含两类目标，以及同一录屏没有跨集合。它不会加载 YOLO、下载 `yolo11n.pt` 或开始训练。输出 `READY` 才表示数据结构允许进入训练。

## 训练

官方基线参数位于 `configs/default.toml`。第三轮实际执行参数保存在 `configs/pending.toml`，使用已保留的两个 `best-init/best.pt` 初始化：

| 参数 | locator | minigame |
|---|---:|---:|
| 输入尺寸 | `960 x 960` | `640 x 640` |
| epochs | 100 | 100 |
| batch | 4 | 8 |
| patience | 20 | 20 |

两者均使用 `device=0`、`workers=4`、`seed=42`、`pretrained=true`、`plots=true` 和 AMP。locator 提高分辨率是为了保留完整屏幕中缩放动画较小时的感叹号细节，batch 相应降为 4 控制显存。未列出的优化器、学习率和增强参数采用锁定的 Ultralytics 8.4.118 默认值；当前数据量下 `optimizer=auto` 会选择 AdamW，并自动决定初始学习率。正式模型卡必须保存实际运行产生的完整参数，不能只抄本表。

2026-08-14 加入 `屏幕录制 2026-08-14 032049` 和局部困难负样本后，训练前预检为 `READY`，第三轮训练已经完成。`屏幕录制 2026-08-12 223804` 与 `感叹号验证集` 都参与过训练/验证流程，只能用于训练过程复验，不能再作为最终独立审核视频。新提交的 `training/test/videos/屏幕录制 2026-08-14 225423.mp4` 未出现在两个 `split.json`、训练数据文件名或数据处理输出中，作为本轮独立审核素材。同一段录屏没有跨越同一任务的 train/val。

| 任务 | train 录屏/图片 | val 录屏/图片 | train 类别框 | val 类别框 |
|---|---:|---:|---:|---:|
| locator | 4 / 1,443 | 2 / 269 | `bite_indicator=129`、`minigame_panel=498` | `bite_indicator=80`、`minigame_panel=67` |
| minigame | 3 / 598 | 1 / 81 | `catch_zone=498`、`moving_target=498` | `catch_zone=67`、`moving_target=67` |

locator 和 minigame 均使用拆分种子 `1` 固定历史录屏划分；新增的 `感叹号验证集` 已人工审核后明确加入 Locator `val`，没有重新随机划分。locator train/val 的正负样本为 `626/817` 和 `147/122`；minigame 为 `498/100` 和 `67/14`。训练随机种子仍为 `42`。`READY` 只表示目录、标签和划分结构合法。Round 3 的对比报告是在新增这段验证录屏之前生成的，不能直接代表加入新验证数据后的指标。

```powershell
uv run vrc-preflight --config configs/pending.toml --task all
```

人工批准数据和参数后执行；下列命令已于 2026-08-14 完成：

```powershell
uv run vrc-train --config configs/pending.toml --task all --confirm-reviewed
```

第三轮配置只改变数据集和初始权重路径，保持上一轮训练参数不变；运行目录为 `locator-round3` 和 `minigame-round3`。Locator 在第 22 轮早停、最佳为第 2 轮；Minigame 在第 25 轮早停、最佳为第 5 轮。同名目录已存在时训练入口直接停止，不覆盖也不自动改名。

旧、新权重已经在当前完全相同的验证集上重新评估。完整指标、差值和结论见 [`reports/round3.md`](reports/round3.md)；PR/F1 曲线和混淆矩阵保存在本机 `runs/round3-comparison/`。Minigame 第三轮明确优于上一轮；Locator 的面板定位改善，但感叹号验证实例只有 6 个，尚不足以决定是否替换上一轮 Locator。

新增的 `感叹号验证集` 已单独完成训练前基线验证，结果见 [`reports/round4-baseline.md`](reports/round4-baseline.md)。这段视频是一个持续事件的缩放动画，不能把 74 张帧当作 74 个独立事件。在软件默认 `ConfidenceThreshold=0.35` 下，`locator-best-init` 的逐帧 Recall 为 95.95%，且漏帧互不连续；Round 3 为 89.19% 且出现连续漏帧。因此当前运行基线继续使用 `locator-best-init`，暂不因这批验证数据直接重训。

首次执行会把官方 `yolo11n.pt` 下载到当前 `training/` 目录。下载的基础权重和未验收训练结果均被 `.gitignore` 排除。只有最终选定并完成验收的两个 `best.pt` 会由发布脚本重命名后写入 `models/vX.Y.Z/checkpoints/`，与对应 ONNX 一起提交到仓库。

也可以单独训练：

```powershell
uv run vrc-train --task locator --confirm-reviewed
uv run vrc-train --task minigame --confirm-reviewed
```

`--confirm-reviewed` 是强制人工确认门。即使带上该参数，训练入口仍会再次执行相同预检；任何数据错误都会在加载模型和下载权重前停止。

Ultralytics 结果写入 `runs/locator*` 和 `runs/minigame*`。每次运行会生成新的目录；不要假定权重总在未带编号的路径中。

## 当前训练结果

2026-08-14 已在 RTX 4060 Laptop GPU 上完成首轮基线训练。locator 在第 48 轮早停，最佳轮次为 28；minigame 在第 46 轮早停，最佳轮次为 26。

| 模型 | Precision | Recall | mAP50 | mAP50-95 | 权重 |
|---|---:|---:|---:|---:|---|
| locator | 0.849 | 0.917 | 0.898 | 0.697 | `runs/locator/weights/best.pt` |
| minigame | 0.969 | 0.688 | 0.953 | 0.635 | `runs/minigame/weights/best.pt` |

这两个权重是首轮实验基线，后续已用于继续训练和对照；本表用于说明历史基线，不是当前发布模型：

- locator 的独立验证集只有 6 个 `bite_indicator`，指标波动较大；该类 Recall 为 0.833、mAP50 为 0.807。
- minigame 的 `catch_zone` Recall 为 0.985，但 `moving_target` Recall 只有 0.392。即使把置信度阈值降至 0.005，召回率仍未提高。
- minigame 训练录屏主要含灰色和红色目标，独立验证录屏大量含绿色鱼形目标，存在明显外观域偏移。应补齐各种目标外观的独立录屏后重新划分和训练，不能只调低运行阈值。
- 这些限制解释了为什么后续必须继续补数据、统一复验并进行完整视频人工审核。

同日加入 `屏幕录制 2026-08-14 073419` 后完成第二轮双初始化对照。四组使用相同数据、验证录屏、训练参数和随机种子，只改变初始权重；下表来自训练结束后对各自 `best.pt` 的统一复验：

| 运行 | 初始化 | 完成/最佳 epoch | Precision | Recall | mAP50 | mAP50-95 |
|---|---|---:|---:|---:|---:|---:|
| `locator-official` | `yolo11n.pt` | 53 / 33 | 0.954 | 0.917 | 0.915 | 0.767 |
| `locator-best-init` | 首轮 locator `best.pt` | 73 / 53 | 0.986 | 0.917 | 0.975 | 0.765 |
| `minigame-official` | `yolo11n.pt` | 38 / 18 | 0.947 | 0.958 | 0.981 | 0.667 |
| `minigame-best-init` | 首轮 minigame `best.pt` | 22 / 2 | 0.920 | 0.904 | 0.980 | 0.690 |

关键类别复验结果：

| 运行/类别 | Precision | Recall | mAP50 | mAP50-95 |
|---|---:|---:|---:|---:|
| `locator-official` / `bite_indicator` | 0.910 | 0.833 | 0.835 | 0.751 |
| `locator-best-init` / `bite_indicator` | 0.973 | 0.833 | 0.955 | 0.775 |
| `minigame-official` / `moving_target` | 0.984 | 0.930 | 0.978 | 0.590 |
| `minigame-best-init` / `moving_target` | 1.000 | 0.823 | 0.979 | 0.587 |

当前发布模型为 `runs/locator-round3/weights/best.pt` 与 `runs/minigame-round3/weights/best.pt`，已固化到 `../models/v0.1.0/`。Round3 的正式验证指标和与上一轮对比见 [`reports/round3.md`](reports/round3.md)。minigame 中鼠标直接控制 `catch_zone`，目标是使其包住 `moving_target`；完整视频仍需人工检查两类的连续性、漏检和框抖动。

这对发布模型已导出为 `exports/locator.onnx` 与 `exports/minigame.onnx`，并复制到 `../app/models/` 供 C# 开发验证。两者均为 FP32、opset 17、静态 batch 1、无内置 NMS：locator `[1,3,960,960] -> [1,6,18900]`，minigame `[1,3,640,640] -> [1,6,8400]`。完整来源、哈希和许可证见 `../models/v0.1.0/` 与 `MODEL_CARD.md`。

## 完整视频审核

```powershell
uv run vrc-review-video `
  --input "test/videos/<录屏文件>.mp4" `
  --locator "runs/<locator-run>/weights/best.pt" `
  --minigame "runs/<minigame-run>/weights/best.pt" `
  --output "test/results/<录屏文件名>-review.mp4"
```

每帧先做全屏 locator，检测到 `minigame_panel` 后裁剪局部图运行 minigame，再将 `catch_zone`、`moving_target` 框映射回原始大屏。输出保留原始分辨率，默认不复制音频。Python 审核使用 `.pt`；C# 正式运行使用导出的 ONNX。

视频审核默认按 locator `960`、minigame `640` 推理；可用 `--locator-image-size` 与 `--minigame-image-size` 显式覆盖，仅用于对比实验。

## 导出

审核两个任务实际产生的 `best.pt` 后，用明确路径导出：

```powershell
.venv\Scripts\vrc-export.exe `
  --locator runs\<locator-run>\weights\best.pt `
  --minigame runs\<minigame-run>\weights\best.pt
```

导出器默认读取 `configs/default.toml`，将 locator 固定导出为 `960 x 960`、minigame 固定导出为 `640 x 640`。需要实验性覆盖时分别使用 `--locator-image-size` 和 `--minigame-image-size`，不再提供一个会同时改动两个模型的全局尺寸参数。

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

当前导出文件实测为：locator 10,815,002 字节，minigame 10,604,959 字节，合计 20.43 MiB。人工审核视频位于：

```text
test/results/屏幕录制 2026-08-14 225423-onnx-review.mp4
```

该视频来自未进入训练/验证清单的录屏。由于 3048 帧逐帧推理耗时过长，本次使用 `--inference-stride 3`：每 3 帧运行一次 ONNX，输出容器保持 3048 帧、原始 30 FPS 和原时长，但每组后两帧复用最近一张已标注画面，视觉更新约为 10 Hz。统计的 394 个 locator 和 486 个 minigame 检测是 1016 个推理帧上的候选框计数，不是逐帧指标。审核视频只是人工验收材料，不能计算 mAP、Precision 或 Recall；它不阻止维护者将当前最佳权重固化为 `models/v0.1.0/`，但不能据此宣称自动钓鱼已验收。

`.pt` 用于开发时训练与实验，用户软件只加载两个 ONNX。CPU-only 与 DirectML 使用同一份 ONNX，不需要重新训练。

同一 ONNX 也可由 ONNX Runtime CUDA Provider 加载；切换 CPU、DirectML 或 CUDA Provider 不需要重新训练。FP16、INT8 或 TensorRT 属于另行转换和兼容性验证，不等于重新训练。

将 `.pt` 转成 ONNX 或将模型作为独立下载，不会改变其上游许可证。正式模型版本必须先把两个 `.pt`、两个 ONNX、`MODEL_CARD.md`、完整 `MODEL_LICENSE.txt`（内容来自本目录 `LICENSE`）和源码清单提交到 `models/vX.Y.Z/`；模型 Release 只能发布该目录中运行时文件的相同副本。

模型设计、数据停止条件和验收指标见 [视觉、数据与训练](../docs/vision-and-training.md)。
