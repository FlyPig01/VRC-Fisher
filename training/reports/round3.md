# Round 3 模型对比

评估日期：2026-08-14。旧、新权重均在当前完全相同的验证集上重新评估。指标计算固定使用 CUDA、Locator 960、Minigame 640、`conf=0.001`、`iou=0.7`；`conf=0.001` 是绘制完整 PR 曲线的验证阈值，不是软件运行阈值。

## 训练结果

| 任务 | 初始化权重 | 实际轮数 | 最佳轮次 | 记录用时 | 最佳权重 |
|---|---|---:|---:|---:|---|
| Locator | `locator-best-init/best.pt` | 22 | 2 | 894.6 s | `runs/locator-round3/weights/best.pt` |
| Minigame | `minigame-best-init/best.pt` | 25 | 5 | 184.2 s | `runs/minigame-round3/weights/best.pt` |

两项任务均因连续 20 轮没有刷新最佳 `mAP50-95` 而早停。训练未出现 NaN、损坏图片或标签错误。

## Locator

验证集为 194 张图片，其中 121 张负样本；`bite_indicator` 只有 6 个实例，`minigame_panel` 有 67 个实例。

| 范围 | 指标 | 上一轮 | Round 3 | 差值 |
|---|---|---:|---:|---:|
| 总体 | Precision | 98.59% | 99.84% | +1.26 pp |
| 总体 | Recall | 91.67% | 90.92% | -0.74 pp |
| 总体 | mAP50 | 97.50% | 95.86% | -1.64 pp |
| 总体 | mAP50-95 | 76.55% | 79.81% | +3.26 pp |
| 感叹号 | Precision | 97.28% | 100.00% | +2.72 pp |
| 感叹号 | Recall | 83.33% | 81.85% | -1.48 pp |
| 感叹号 | mAP50 | 95.50% | 92.23% | -3.27 pp |
| 感叹号 | mAP50-95 | 77.50% | 76.07% | -1.43 pp |
| 面板 | Precision | 99.90% | 99.69% | -0.21 pp |
| 面板 | Recall | 100.00% | 100.00% | 0.00 pp |
| 面板 | mAP50 | 99.50% | 99.50% | 0.00 pp |
| 面板 | mAP50-95 | 75.59% | 83.54% | +7.95 pp |

Round 3 明显改善了面板框定位，且总体误检更少；但感叹号 Recall、mAP50 和 mAP50-95 均略降。验证集只有 6 个感叹号，单个实例就能造成约 16.7 个百分点的原始召回变化，本轮不能证明感叹号能力提升，也不能可靠证明退步。Locator 暂不应仅凭该验证集替换上一轮，需要独立感叹号验证录屏或视频人工审核。

## Minigame

验证集为 81 张图片，其中 14 张负样本；`catch_zone` 和 `moving_target` 各有 67 个实例。

| 范围 | 指标 | 上一轮 | Round 3 | 差值 |
|---|---|---:|---:|---:|
| 总体 | Precision | 82.85% | 98.25% | +15.40 pp |
| 总体 | Recall | 78.59% | 98.51% | +19.92 pp |
| 总体 | mAP50 | 96.42% | 98.07% | +1.65 pp |
| 总体 | mAP50-95 | 66.43% | 71.82% | +5.39 pp |
| `catch_zone` | Precision | 65.70% | 98.04% | +32.34 pp |
| `catch_zone` | Recall | 98.51% | 98.51% | 0.00 pp |
| `catch_zone` | mAP50-95 | 73.42% | 80.71% | +7.29 pp |
| `moving_target` | Precision | 100.00% | 98.46% | -1.54 pp |
| `moving_target` | Recall | 58.66% | 98.51% | +39.84 pp |
| `moving_target` | mAP50-95 | 59.44% | 62.93% | +3.49 pp |

Round 3 明确优于上一轮。`moving_target` Recall 从 58.66% 提升到 98.51%，直接对应上一轮严重漏识别问题；`catch_zone` Precision 从 65.70% 提升到 98.04%，说明局部负样本显著减少了误识别。建议 Round 3 作为新的 Minigame 候选权重，之后再用完整视频人工验收稳定性。

## 选择结论

- Minigame：选择 `runs/minigame-round3/weights/best.pt`。
- Locator：保留 `runs/locator-round3/weights/best.pt` 作为面板定位候选，但暂不淘汰 `locator-best-init/best.pt`。先补充至少 50 个未参与训练的感叹号验证实例，再做最终选择。
- 四组 PR/F1 曲线、混淆矩阵和验证预测图保存在本机 `runs/round3-comparison/` 对应子目录中。

机器可读原始数值和四个权重的 SHA-256 见 [round3-metrics.json](round3-metrics.json)。
