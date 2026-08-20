# 视觉与训练

本文定义双模型、类别、数据集和验收契约。具体数据命令见 [数据处理](../data_processing/README.md)，训练命令见 [模型训练](../training/README.md)。

## 1. 模型契约

| 模型 | 输入 | 类别 | 职责 |
|---|---:|---|---|
| `locator.onnx` | `960 x 960` 全屏 | `bite_indicator`、`minigame_panel` | 定位感叹号和小游戏面板 |
| `minigame.onnx` | `640 x 640` 面板裁剪 | `catch_zone`、`moving_target` | 定位滑块和移动目标 |

鱼、齿轮等被控制目标统一标为 `moving_target`。不标注成功、失败、轨道、进度条或装饰元素。

软件使用静态 batch 1、FP32、无内置 NMS 的 ONNX。CPU 与 DirectML 加载同一模型，不需要重新训练。

## 2. 数据契约

人工只标注完整屏幕图片：

- 感叹号画面标 `bite_indicator`；
- 小游戏画面标 `minigame_panel`、`catch_zone`、`moving_target`；
- minigame 数据由工具按 `minigame_panel` 自动裁剪并转换类别；
- 已审核的空标签图片可作为负样本；
- 未审核、误标或没有同名标签的图片不得进入数据集。

`bite_indicator` 与 `minigame_panel` 是互斥状态，同一张图片不能同时标注这两个 locator 类别。只有画面中既没有感叹号、也没有小游戏面板时，图片才可以使用空标签作为 locator 背景负样本；画面中出现小游戏面板时，必须标注 `minigame_panel`，不能为了训练感叹号而把整张图片标成空标签。

训练集和验证集使用固定种子的图片级分层随机划分，默认 `90% train / 10% val`。同一连续录屏可以跨集合，使长录屏和用户反馈画面充分参与训练；同一张图片不得重复进入两个集合。独立测试视频不得参与训练或验证；无真值视频只能人工检查，不能计算 Precision、Recall 或 mAP。

## 3. 训练契约

当前训练链为 Python 3.11、PyTorch、Ultralytics YOLO11n。训练前必须满足：

1. 数据处理审计通过；
2. `train` 与 `val` 均包含所需类别；
3. `split.json` 与实际图片一致，且同一张图片不跨集合；
4. 人工明确批准数据和参数；
5. 训练入口再次执行预检。

数据不足、类别错误、图片重复分配或审核未完成时必须停止。

## 4. 模型验收

模型验收不能只看总体 mAP，各类别的优先级不同：

- `bite_indicator` 首先看 Precision，必须尽量避免错识别。感叹号会持续缩放并被多次检测，少量漏检通常只会推迟响应；误识别则可能错误推进钓鱼流程；
- `minigame_panel` 首先看 Recall，漏检会阻止软件进入或维持小游戏阶段；
- `catch_zone` 和 `moving_target` 首先看 Recall，同时看 mAP50-95 和视频中的框稳定性，漏框或边界漂移都会影响控制。

还必须通过完整视频检查：

- 感叹号是否产生会错误推进流程的误识别；
- 面板移动后的重新定位；
- `catch_zone` 与 `moving_target` 的边界误差和抖动；
- 独立完整视频中的漏检和误检；
- C# ONNX 回放与真实 VRChat 流程。

模型来源、指标和哈希写入对应版本的模型卡，不在本文记录具体实验结果。验收通过后的模型按 [发布与许可](release.md) 和 [打包说明](../packaging/README.md) 生成发布物。
