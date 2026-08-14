# 完整大屏视频审核

`videos/` 放未经标注的完整显示器录屏；它们不属于 YOLO 的 train/val 数据集，也不需要 YOLO TXT。`results/` 保存双模型推理后绘制了完整检测框的视频，供人工检查漏检、误检、框抖动和状态连续性。

## 使用

在 `training/` 目录执行，训练完成后把两个 `best.pt` 路径传入：

```powershell
uv run vrc-review-video `
  --input "test/videos/<录屏文件>.mp4" `
  --locator "runs/<locator-run>/weights/best.pt" `
  --minigame "runs/<minigame-run>/weights/best.pt" `
  --output "test/results/<录屏文件名>-review.mp4"
```

可用 `--device cpu`（默认）或 Ultralytics 支持的设备值，例如 `--device 0`。Python 审核使用 `.pt`；C# 运行时使用导出的 ONNX。

默认输入尺寸与训练配置一致：locator `960 x 960`，minigame `640 x 640`。

流程：原始大屏帧 -> locator 检测 `bite_indicator`/`minigame_panel` -> 按 panel 裁剪 -> minigame 检测 `catch_zone`/`moving_target` -> 坐标映射回大屏 -> 输出视频。输出保持原始分辨率，默认不复制音频。

视频没有真值标签，不能计算 mAP、Precision 或 Recall，只用于训练后的人工端到端验收。视频和结果均被 Git 忽略。
