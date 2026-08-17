# Round 5 训练与统一验证记录

日期：2026-08-18
状态：训练完成；已选择 Locator Round 5 与 Minigame Round 3 组成 `models-v0.1.1`。独立视频人工审核仍是上传正式 GitHub Release 资产前的质量检查。

## 本轮范围

本轮在人工审核完成后，将 `8月18日.mp4` 加入训练集，并分别训练 Locator 与 Minigame。新录屏只进入训练集，验证录屏保持独立；同一录屏没有跨越 `train` 与 `val`，避免相邻视频帧泄漏。

| 新增标注内容 | 数量 |
|---|---:|
| 审核图片 | 286 |
| `bite_indicator` | 57 |
| `minigame_panel` | 200 |
| `catch_zone` | 200 |
| `moving_target` | 200 |
| 无任何目标的图片 | 29 |

新增录屏对 Locator 提供 257 张正样本和 29 张负样本；对 Minigame 提供 200 张正样本和 86 张负样本，其中感叹号画面也作为 Minigame 负样本。

## 数据集

### Locator

| 划分 | 录屏数 | 图片 | 正样本 | 负样本 | `bite_indicator` 框 | `minigame_panel` 框 |
|---|---:|---:|---:|---:|---:|---:|
| train | 5 | 1,729 | 883 | 846 | 186 | 698 |
| val | 2 | 269 | 147 | 122 | 80 | 67 |
| 合计 | 7 | 1,998 | 1,030 | 968 | 266 | 765 |

| 划分 | 录屏 |
|---|---|
| train | `屏幕录制 2026-08-14 032049`、`20260812-2035-32.2147786`、`屏幕录制 2026-08-14 073419`、`屏幕录制 2026-08-14 052207`、`8月18日` |
| val | `感叹号验证集`、`屏幕录制 2026-08-12 223804` |

划分参数为 seed 13、录屏比例 0.7/0.3；受各录屏帧数影响，最终图片比例为 86.5%/13.5%。

### Minigame

| 划分 | 录屏数 | 图片 | 正样本 | 负样本 | `catch_zone` 框 | `moving_target` 框 |
|---|---:|---:|---:|---:|---:|---:|
| train | 4 | 827 | 698 | 129 | 698 | 698 |
| val | 1 | 81 | 67 | 14 | 67 | 67 |
| 合计 | 5 | 908 | 765 | 143 | 765 | 765 |

| 划分 | 录屏 |
|---|---|
| train | `8月18日`、`20260812-2035-32.2147786`、`屏幕录制 2026-08-14 032049`、`屏幕录制 2026-08-14 073419` |
| val | `屏幕录制 2026-08-12 223804` |

划分参数为 seed 13、录屏比例 0.8/0.2；受录屏数量和帧数影响，最终图片比例为 91.1%/8.9%。

## 训练环境与参数

| 项目 | Locator | Minigame |
|---|---|---|
| 初始化权重 | `locator-best-init/weights/best.pt` | `minigame-best-init/weights/best.pt` |
| 架构 | YOLO11n | YOLO11n |
| 输入尺寸 | 960 × 960 | 640 × 640 |
| 最大轮数 | 100 | 100 |
| Batch | 4 | 8 |
| Early stopping patience | 20 | 20 |
| 优化器 | Auto，实际选择 AdamW | Auto，实际选择 AdamW |
| 初始学习率 | 0.001667 | 0.001667 |
| Momentum | 0.9 | 0.9 |
| AMP | 开启 | 开启 |
| 随机种子 | 42 | 42 |
| Workers | 4 | 4 |
| 设备 | NVIDIA GeForce RTX 4060 Laptop GPU，CUDA 0 | NVIDIA GeForce RTX 4060 Laptop GPU，CUDA 0 |

训练环境为 Python 3.11.4、Ultralytics 8.4.118、PyTorch 2.13.0+cu130。AMP 的联网兼容性自检因 GitHub 证书连接失败而跳过，但本地 AMP 训练正常完成，结果中没有 NaN 或零指标；这不是训练故障。

## 训练结果

最佳轮次按验证集 `mAP50-95` 选择。记录用时为 `results.csv` 中的累计时间，不包含两项任务之间的进程启动时间。

| 任务 | 实际轮数 | 最佳轮次 | Precision | Recall | mAP50 | mAP50-95 | 累计用时 | 早停 |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| Locator Round 5 | 64 | 44 | 97.974% | 99.375% | 98.938% | 82.004% | 44 分 29.46 秒 | 是 |
| Minigame Round 5 | 22 | 2 | 97.689% | 97.045% | 98.365% | 71.187% | 3 分 32.82 秒 | 是 |
| 合计 | 86 | — | — | — | — | — | 48 分 02.28 秒 | — |

| 任务 | 最佳权重 | 大小 |
|---|---|---:|
| Locator | `training/runs/locator-round5/weights/best.pt` | 5.24 MiB |
| Minigame | `training/runs/minigame-round5/weights/best.pt` | 5.17 MiB |

## 同验证集对比

三代权重均在本轮当前验证集上重新验证，固定使用 CUDA、`conf=0.001`、`iou=0.7`；Locator 输入 960，Minigame 输入 640。这里的低置信度用于生成完整 PR 曲线，不是软件运行阈值。表中数值按统一验证输出保留到 0.1 个百分点。

### Locator 总体

| 权重 | Precision | Recall | mAP50 | mAP50-95 |
|---|---:|---:|---:|---:|
| best-init | 98.4% | 96.3% | 98.3% | 74.2% |
| Round 3 | 97.0% | 94.6% | 98.4% | 78.1% |
| **Round 5** | **98.0%** | **99.4%** | **98.9%** | **82.1%** |

### Locator 分类别

| 类别 | 权重 | Precision | Recall | mAP50 | mAP50-95 |
|---|---|---:|---:|---:|---:|
| `bite_indicator` | best-init | 97.2% | 92.5% | 97.0% | 72.7% |
| `bite_indicator` | Round 3 | 94.7% | 89.1% | 97.4% | 72.6% |
| `bite_indicator` | **Round 5** | **97.3%** | **98.8%** | **98.4%** | **80.4%** |
| `minigame_panel` | best-init | 99.7% | 100.0% | 99.5% | 75.6% |
| `minigame_panel` | Round 3 | 99.4% | 100.0% | 99.5% | 83.5% |
| `minigame_panel` | **Round 5** | 98.7% | 100.0% | 99.5% | **83.7%** |

Round 5 的感叹号 Recall 比 Round 3 提高 9.7 个百分点，严格定位指标提高 7.8 个百分点；面板 Recall 保持 100%，严格定位略有提高。Locator Round 5 是三者中最合适的候选权重。

### Minigame 总体

| 权重 | Precision | Recall | mAP50 | mAP50-95 |
|---|---:|---:|---:|---:|
| best-init | 82.9% | 78.6% | 96.4% | 66.4% |
| **Round 3** | **98.2%** | **98.5%** | 98.1% | **71.8%** |
| Round 5 | 97.7% | 97.1% | **98.4%** | 71.2% |

### Minigame 分类别

| 类别 | 权重 | Precision | Recall | mAP50 | mAP50-95 |
|---|---|---:|---:|---:|---:|
| `catch_zone` | best-init | 65.7% | 98.5% | 97.0% | 73.4% |
| `catch_zone` | **Round 3** | **98.0%** | **98.5%** | **98.7%** | **80.7%** |
| `catch_zone` | Round 5 | 97.0% | 95.7% | 98.3% | 80.3% |
| `moving_target` | best-init | **100.0%** | 58.7% | 95.9% | 59.4% |
| `moving_target` | **Round 3** | **98.5%** | **98.5%** | 97.4% | **62.9%** |
| `moving_target` | Round 5 | 98.4% | **98.5%** | **98.4%** | 62.1% |

Round 5 提高了 Minigame 的 mAP50，但总体 Precision、Recall 和 mAP50-95 均略低于 Round 3。尤其 `catch_zone` Recall 由 98.5% 降到 95.7%；控制器依赖连续、准确的滑块框，因此这项下降比 0.3 个百分点的总体 mAP50 提升更重要。`moving_target` 仍是严格框定位较弱的类别。

## 结论

| 任务 | 当前推荐 | 原因 |
|---|---|---|
| Locator | `locator-round5/weights/best.pt` | 感叹号 Recall 和严格定位明显提升，面板能力保持稳定 |
| Minigame | 暂时保留 `minigame-round3/weights/best.pt` | Round 5 没有超过 Round 3，且 `catch_zone` Recall 略降 |

Precision 反映误识别，Recall 反映漏识别，mAP50 反映目标是否大致找到，mAP50-95 更重视识别框是否贴合。对本项目而言：感叹号首先重视 Recall；小游戏控制同时重视 `catch_zone`、`moving_target` 的 Recall 和 mAP50-95，因为漏框或框位置漂移都会直接影响控制。

本轮训练指标只完成候选模型筛选，不能单独关闭模型质量问题。正式替换模型前，仍需由人工使用未参与训练与验证的完整视频检查：感叹号事件是否整段漏检、视角变化后是否能重新定位、小游戏框是否稳定贴合，以及是否产生影响流程的误识别。

## 产物

| 内容 | 路径 |
|---|---|
| 保留的 Locator 训练记录 | `training/runs/locator-round5/` |
| 保留的 Minigame 训练记录 | `training/runs/minigame-round3/` |
| 未入选的 Minigame Round 5 | 指标保留在本文，训练目录已在选型后删除 |
| 本轮训练配置 | `training/configs/round5.toml` |
| Locator 数据划分 | `training/datasets/locator/split.json` |
| Minigame 数据划分 | `training/datasets/minigame/split.json` |
| 正式源码模型 | `models/v0.1.1/` |
| 本地 Release 资产 | `releases/models-v0.1.1/` |
