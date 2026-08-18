# Round 6 训练与统一验证记录

日期：2026-08-18
状态：训练与导出完成；Locator 和 Minigame 均选择 Round 6 `best.pt` 组成 `models-v0.1.2`。

## 数据集

本轮使用图片级分层随机划分，固定随机种子 `42`，比例为 `90% train / 10% val`。正样本与负样本分别随机分配，同一张图片不会同时进入两个集合。

| 任务 | 划分 | 图片 | 正样本 | 负样本 | 类别 0 | 类别 1 |
|---|---|---:|---:|---:|---:|---:|
| Locator | train | 2,076 | 1,070 | 1,006 | `bite_indicator` 244 | `minigame_panel` 829 |
| Locator | val | 232 | 120 | 112 | `bite_indicator` 28 | `minigame_panel` 93 |
| Minigame | train | 977 | 830 | 147 | `catch_zone` 830 | `moving_target` 830 |
| Minigame | val | 108 | 92 | 16 | `catch_zone` 92 | `moving_target` 92 |

## 环境与参数

| 项目 | Locator Round 6 | Minigame Round 6 |
|---|---|---|
| 初始化权重 | `locator-round5/weights/best.pt` | `minigame-round3/weights/best.pt` |
| 架构 | YOLO11n | YOLO11n |
| 输入尺寸 | 960 x 960 | 640 x 640 |
| 最大轮数 | 100 | 100 |
| Batch | 4 | 8 |
| Early stopping patience | 20 | 20 |
| 优化器 | Auto，实际为 AdamW | Auto，实际为 AdamW |
| AMP | 开启 | 开启 |
| 随机种子 | 42 | 42 |
| Workers | 4 | 4 |
| 设备 | NVIDIA GeForce RTX 4060 Laptop GPU，CUDA 0 | NVIDIA GeForce RTX 4060 Laptop GPU，CUDA 0 |

训练环境为 Python 3.11.4、Ultralytics 8.4.118、PyTorch 2.13.0+cu130。AMP 联网自检因 GitHub SSL 证书失败而跳过，本地 AMP 训练正常完成，没有出现 NaN、显存溢出或数据读取错误。

## 训练结果

Ultralytics 使用 `0.1 x mAP50 + 0.9 x mAP50-95` 选择 `best.pt`。两项任务都在最佳轮次后连续 20 轮没有提升，因此触发早停。

| 任务 | 实际轮数 | 最佳轮次 | Precision | Recall | mAP50 | mAP50-95 | 训练用时 |
|---|---:|---:|---:|---:|---:|---:|---:|
| Locator Round 6 | 77 | 57 | 98.342% | 92.857% | 98.330% | 87.284% | 66 分 07.32 秒 |
| Minigame Round 6 | 99 | 79 | 99.916% | 100.000% | 99.500% | 83.662% | 20 分 30.87 秒 |
| 合计 | 176 | - | - | - | - | - | 86 分 38.19 秒 |

| 权重 | 大小 |
|---|---:|
| `training/runs/locator-round6/weights/best.pt` | 5.24 MiB |
| `training/runs/locator-round6/weights/last.pt` | 5.24 MiB |
| `training/runs/minigame-round6/weights/best.pt` | 5.18 MiB |
| `training/runs/minigame-round6/weights/last.pt` | 5.18 MiB |

## 同一验证集复测

旧权重和新权重均在本轮当前验证集上重新验证。Locator 固定输入 960，Minigame 固定输入 640；因此下表可以直接比较。验证指标用于生成完整 PR 曲线，不等同于软件运行时的置信度阈值。

### Locator

| 权重 | 类别 | Precision | Recall | mAP50 | mAP50-95 |
|---|---|---:|---:|---:|---:|
| Round 5 `best.pt` | 总体 | 97.0% | 93.4% | 94.1% | 76.1% |
| Round 5 `best.pt` | `bite_indicator` | 95.3% | **96.4%** | 96.2% | 75.6% |
| Round 5 `best.pt` | `minigame_panel` | 98.8% | 90.3% | 92.0% | 76.7% |
| Round 6 `best.pt` | 总体 | **99.8%** | 92.6% | **98.3%** | **87.2%** |
| Round 6 `best.pt` | `bite_indicator` | **100.0%** | 85.1% | **97.2%** | **85.7%** |
| Round 6 `best.pt` | `minigame_panel` | 99.7% | **100.0%** | **99.5%** | **88.7%** |
| Round 6 `last.pt` | 总体 | 97.7% | **96.3%** | 97.0% | 85.7% |
| Round 6 `last.pt` | `bite_indicator` | 96.3% | 92.6% | 94.5% | 83.0% |
| Round 6 `last.pt` | `minigame_panel` | **99.2%** | **100.0%** | **99.5%** | 88.3% |

本项目对感叹号首先要求避免错识别，漏识别只会推迟响应，因此 `bite_indicator` 优先比较 Precision。Round 6 `best.pt` 的感叹号 Precision 从 95.3% 提高到 100.0%，同时面板 Recall 从 90.3% 提高到 100.0%，符合两个类别各自的应用目标。它的感叹号 Recall 降至 85.1%，需要在人工视频审核中确认不会造成无法接受的等待，但不再作为否决条件。Round 6 `last.pt` 的感叹号 Precision 只有 96.3%，不优于 `best.pt`。

### Minigame

| 权重 | 类别 | Precision | Recall | mAP50 | mAP50-95 |
|---|---|---:|---:|---:|---:|
| Round 3 `best.pt` | 总体 | 96.1% | 90.6% | 96.6% | 72.4% |
| Round 3 `best.pt` | `catch_zone` | 92.2% | 89.5% | 94.9% | 67.8% |
| Round 3 `best.pt` | `moving_target` | 100.0% | 91.7% | 98.4% | 76.9% |
| Round 6 `best.pt` | 总体 | **99.9%** | **100.0%** | **99.5%** | **83.5%** |
| Round 6 `best.pt` | `catch_zone` | **99.9%** | **100.0%** | **99.5%** | **83.9%** |
| Round 6 `best.pt` | `moving_target` | **99.9%** | **100.0%** | **99.5%** | **83.1%** |

Minigame Round 6 在两个类别的 Precision、Recall 和严格框定位上都明显超过 Round 3，可作为下一步人工视频审核的首选候选。

## 结论

| 任务 | 当前处理 | 原因 |
|---|---|---|
| Locator | 推荐 Round 6 `best.pt` 进入人工视频审核 | 感叹号 Precision 达到 100%，面板 Recall 达到 100%，同时严格框定位明显提高 |
| Minigame | 推荐 Round 6 `best.pt` 进入人工视频审核 | 两个类别的 Recall 均达到 100%，mAP50-95 提高 11.1 个百分点 |

随机图片划分让各段反馈素材充分进入训练，但相邻视频帧也可能分布在训练集和验证集，构成时间相邻帧的数据泄漏。这里列出的指标会偏高，只适合比较当前划分上的候选权重，不能代表新录屏的真实泛化能力，也不能替代完整视频人工审核。

## 产物

| 内容 | 路径 |
|---|---|
| 本轮配置 | `training/configs/pending.toml` |
| Locator 训练目录 | `training/runs/locator-round6/` |
| Minigame 训练目录 | `training/runs/minigame-round6/` |
| Locator 数据划分 | `training/datasets/locator/split.json` |
| Minigame 数据划分 | `training/datasets/minigame/split.json` |
