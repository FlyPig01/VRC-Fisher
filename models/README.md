# Official Models

该目录保存已经完成数据审核、独立视频验证和许可证检查的官方模型。正式模型必须随源码仓库提交，GitHub Release 只是最终用户的下载渠道，不是模型唯一的公开位置。

每个版本使用固定结构：

```text
models/vX.Y.Z/
  checkpoints/
    locator.pt
    minigame.pt
  runtime/
    locator.onnx
    minigame.onnx
  MODEL_CARD.md
  MODEL_LICENSE.txt
  source-manifest.json
```

`.pt` 是继续训练、检查和修改模型的首选形式；ONNX 是 C# 软件实际加载的运行时形式。两者都按模型卡声明的许可证公开。`source-manifest.json` 记录全部权重、运行时模型和文档的大小与 SHA-256。

这里不能放 `last.pt`、任意 epoch、未验收实验、训练日志或数据集。它们继续留在被 Git 忽略的 `training/runs/`、`training/weights/` 和 `training/exports/`。当前正式源码模型为 `v0.1.3`：Locator Round 7 与 Minigame Round 6。

模型版本通过 `packaging/build-model-release.ps1` 生成。维护者必须提交完整的 `models/vX.Y.Z/` 后，才能上传对应的 `releases/models-vX.Y.Z/`。Release 中只复制软件需要的两个 ONNX、模型卡、许可证和运行时清单，不重复提供 `.pt`；需要继续训练或审计模型的开发者直接从仓库取得 `.pt`。

当前小型 YOLO11n 模型使用普通 Git 提交，不依赖 Git LFS。构建脚本要求每个 `.pt` 和 ONNX 小于 100 MiB，以满足 GitHub 的普通 Git 单文件限制；若未来模型达到该阈值，必须先为 `models/` 正式配置 Git LFS，并同步修改构建脚本和部署文档。
