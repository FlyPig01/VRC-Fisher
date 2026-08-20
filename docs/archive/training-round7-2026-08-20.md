# Round 7 Locator 训练记录

日期：2026-08-20
状态：训练完成；本轮仅重新训练 Locator，Minigame 保留 Round 6。

## 数据集

本轮加入已审核的 `反馈10`，使用图片级分层随机划分，固定种子 `42`，比例为 `90% train / 10% val`。同一录屏可能同时出现在两个集合，因此指标存在时间相邻帧数据泄漏风险。

| 任务 | 划分 | 图片 | 正样本 | 负样本 | 类别 0 | 类别 1 |
|---|---|---:|---:|---:|---:|---:|
| Locator | train | 2,774 | 1,295 | 1,479 | `bite_indicator` 465 | `minigame_panel` 830 |
| Locator | val | 308 | 144 | 164 | `bite_indicator` 52 | `minigame_panel` 92 |
| Locator | 合计 | 3,082 | 1,439 | 1,643 | `bite_indicator` 517 | `minigame_panel` 922 |
| Minigame | train | 977 | 830 | 147 | `catch_zone` 830 | `moving_target` 830 |
| Minigame | val | 108 | 92 | 16 | `catch_zone` 92 | `moving_target` 92 |

`反馈10` 共 268 张：237 张进入 Locator 训练集，31 张进入验证集；其中 241 张为感叹号正样本，27 张为空标签负样本。

## 环境与参数

| 项目 | Locator Round 7 |
|---|---|
| 初始化权重 | `locator-round6/weights/best.pt` |
| 架构 | YOLO11n |
| 输入尺寸 | 960 x 960 |
| 最大轮数 | 100 |
| 实际轮数 | 100 |
| Batch | 4 |
| Early stopping patience | 20 |
| 优化器 | Auto，实际为 AdamW |
| AMP | 开启；联网自检因 GitHub SSL 证书失败而跳过 |
| 随机种子 | 42 |
| Workers | 4 |
| 设备 | NVIDIA GeForce RTX 4060 Laptop GPU，CUDA 0 |

训练环境为 Python 3.11.4、Ultralytics 8.4.118、PyTorch 2.13.0+cu130。训练期间没有出现 NaN、显存溢出、损坏图片或 CUDA 错误。

## 训练结果

| 权重 | 最佳轮次 | Precision | Recall | mAP50 | mAP50-95 | 训练用时 |
|---|---:|---:|---:|---:|---:|---:|
| Round 6 `best.pt` | 57 | 98.342% | 92.857% | 98.330% | 87.284% | - |
| Round 7 `best.pt` | 89 | 99.599% | 99.038% | 99.382% | 87.513% | 132 分 00.65 秒（最佳轮次） |
| Round 7 完整运行 | 100 | 98.772% | 98.077% | 98.925% | 86.194% | 145 分 17.31 秒 |

Round 7 的最佳权重相对 Round 6：Precision 提高 1.257 个百分点，Recall 提高 6.181 个百分点，mAP50 提高 1.052 个百分点，mAP50-95 提高 0.229 个百分点。由于验证集包含与训练集同录屏的相邻帧，以上提升只能说明当前划分上的改进，不能直接代表新视频泛化能力。

## 产物

| 内容 | 路径 |
|---|---|
| 最佳权重 | `training/runs/locator-round7/weights/best.pt` |
| 最后一轮权重 | `training/runs/locator-round7/weights/last.pt` |
| 训练指标 | `training/runs/locator-round7/results.csv` |
| 训练参数 | `training/runs/locator-round7/args.yaml` |
| 训练曲线 | `training/runs/locator-round7/results.png` |
| 混淆矩阵 | `training/runs/locator-round7/confusion_matrix.png` |
| Locator 数据划分 | `training/datasets/locator/split.json` |

SHA-256：

```text
best.pt  A47178F9E87398C6BB35B9E86AAF2DB7AD06FA6BE46DD11BAFB55796EB8B2AD0
last.pt  19F9A0B867DA1CF265F69C03C9E4D06C7A559DAD5FF1E7AA06BB40F8561F4B65
```

## 结论

本轮推荐使用 `locator-round7/weights/best.pt` 进入人工视频审核。它的验证 Recall 相比 Round 6 明显提高，符合当前优先减少感叹号漏识别的目标；但感叹号误识别仍必须通过独立测试视频确认，不能仅依据存在数据泄漏风险的验证指标发布。
