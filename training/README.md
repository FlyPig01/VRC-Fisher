# Training

本目录负责训练、评估和导出两个 YOLO11n 模型。数据处理见 [`data_processing/README.md`](../data_processing/README.md)，模型契约见 [视觉与训练](../docs/vision-and-training.md)。

## 1. 许可证

`training/`、Ultralytics 训练链、官方基础权重及其衍生模型按上游 AGPL-3.0。训练数据默认私有。正式模型必须带模型卡、完整许可证和来源清单，具体规则见 [发布与许可](../docs/release.md)。

## 2. 环境

需要 Windows x64、Python 3.11、uv、可被 `nvidia-smi` 识别的 NVIDIA GPU 和兼容驱动。PyTorch CUDA wheel、Ultralytics 和其他依赖由 `uv.lock` 固定，不需要 Miniconda、全局 CUDA Toolkit、`nvcc` 或 `CUDA_PATH`。

从 `training/` 执行：

```powershell
$Python311 = py -3.11 -c "import sys; print(sys.executable)"
uv sync --locked --python $Python311 --extra dev
uv sync --offline --locked --extra dev
uv run --offline pytest -q
uv run --offline python -c "import torch; print(torch.__version__, torch.version.cuda, torch.cuda.is_available())"
```

本地环境和缓存位于：

```text
training/.venv/
training/.uv-cache/
training/.ultralytics/
```

这些目录、训练结果和导出中间文件不进入用户软件。

## 3. 输入

数据集必须由数据处理工具按图片分层随机划分：

```text
datasets/locator/
  data.yaml
  split.json
  images/{train,val}/
  labels/{train,val}/

datasets/minigame/
  data.yaml
  split.json
  images/{train,val}/
  labels/{train,val}/
```

`split.json` 保存的是每张图片的文件名分配，不是录屏名称；同一录屏可以同时出现在两个集合。

完整大屏测试视频单独放在：

```text
test/videos/
```

测试视频不进入 `data.yaml`。没有真值标签时只能生成带框视频供人工审核。

## 4. 训练前检查

```powershell
uv run vrc-preflight --task all
```

预检检查：

- 图片和 YOLO 标签一一对应；
- 坐标和类别合法；
- locator 与 minigame 都包含所需类别；
- `train`、`val` 使用固定种子的图片级分层随机划分；
- 配置中的输入尺寸、数据路径和基础权重可用。

`READY` 只表示数据结构允许训练，不代表数据质量或模型效果已经合格。未完成人工审核时不得开始训练。

## 5. 当前配置

正式配置以 `configs/pending.toml` 为准：

| 参数 | locator | minigame |
|---|---:|---:|
| 输入尺寸 | 960 | 640 |
| epochs | 100 | 100 |
| batch | 4 | 8 |
| patience | 20 | 20 |
| 初始权重 | locator Round 6 | minigame Round 6 |

公共参数为 `device=0`、`workers=4`、`seed=42`。未显式设置的训练参数使用锁定版本 Ultralytics 的默认值；实际参数必须写入最终模型卡。

## 6. 开始训练

只有人工批准当前数据和参数后执行：

```powershell
uv run vrc-train --config configs/pending.toml --task all --confirm-reviewed
```

单独训练：

```powershell
uv run vrc-train --config configs/pending.toml --task locator --confirm-reviewed
uv run vrc-train --config configs/pending.toml --task minigame --confirm-reviewed
```

训练入口会再次执行预检。同名运行目录存在时停止，不覆盖已有结果。输出位于 `runs/`。

## 7. 评估

训练指标和对比结论放在 `training/reports/`，不写进本 README。当前报告：

- [Round 3](reports/round3.md)
- [Round 4 感叹号基线](reports/round4-baseline.md)

独立视频审核：

```powershell
uv run vrc-review-video `
  --input "test/videos/<video>.mp4" `
  --locator "runs/<locator-run>/weights/best.pt" `
  --minigame "runs/<minigame-run>/weights/best.pt" `
  --output "test/results/<video>-review.mp4"
```

视频审核必须检查连续漏检、误检、面板重定位和框抖动。无真值视频不得报告 Precision、Recall 或 mAP。

## 8. 导出

明确选择两个已验收的 `best.pt` 后执行：

```powershell
uv run vrc-export `
  --locator "runs/<locator-run>/weights/best.pt" `
  --minigame "runs/<minigame-run>/weights/best.pt"
```

输出：

```text
exports/locator.onnx
exports/minigame.onnx
```

locator 固定导出为 960，minigame 固定导出为 640。默认产物为 FP32、静态 batch 1、无内置 NMS。

## 9. 当前已验收模型

当前选择的源码模型版本位于 `models/v0.1.2/`：Locator 与 Minigame 均使用 Round 6 `best.pt`。权重来源、数据规模、指标、数据泄漏限制、导出契约、哈希和许可证只以该目录中的 `MODEL_CARD.md` 与 `source-manifest.json` 为准。

`configs/round6.toml` 保存本轮实际训练配置；`configs/pending.toml` 已切换为 Round 7 入口，并从当前两个最佳权重继续训练。新的训练结果在完成数据审核、统一评估、独立视频检查和 C# 回放前，不得覆盖正式模型版本。
