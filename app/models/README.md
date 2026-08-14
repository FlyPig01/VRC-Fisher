# Runtime Models

该目录只用于 C# 软件开发和回放时放置经过审核的 `locator.onnx` 与 `minigame.onnx`。使用官方模型时还应保留对应的 `MODEL_CARD.md` 与 `MODEL_LICENSE.txt`。正式安装程序不内置模型，用户模型由 `models-v*` GitHub Release 下载到所选安装目录的 `models/`；Release 中的 ONNX 必须与源码仓库 `models/vX.Y.Z/runtime/` 中的文件完全相同。

禁止在本目录放入 `.pt`、检查点、数据集、录屏或训练日志。已验收 `.pt` 的正式开源位置是仓库根目录 `models/vX.Y.Z/checkpoints/`。

官方 ONNX 由 Ultralytics YOLO11 训练链产生，按上游标注的 AGPL-3.0 发布，不属于根目录 MIT License。正式 `models-v*` Release 必须包含 `MODEL_CARD.md` 和 `MODEL_LICENSE.txt`，并将二者写入清单接受完整性校验；开发者放入第三方或自有模型时必须自行确认其许可证与来源。
