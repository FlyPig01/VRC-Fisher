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

训练集和验证集必须按录屏划分，同一连续录屏不能跨集合。独立测试视频不得参与训练或验证；无真值视频只能人工检查，不能计算 Precision、Recall 或 mAP。

## 3. 训练契约

当前训练链为 Python 3.11、PyTorch、Ultralytics YOLO11n。训练前必须满足：

1. 数据处理审计通过；
2. `train` 与 `val` 均包含所需类别；
3. `split.json` 不存在录屏泄漏；
4. 人工明确批准数据和参数；
5. 训练入口再次执行预检。

数据不足、类别错误、划分泄漏或审核未完成时必须停止。

## 4. 模型验收

模型验收不能只看 mAP，还必须检查：

- 感叹号缩放动画中的连续识别；
- 面板移动后的重新定位；
- `catch_zone` 与 `moving_target` 的边界误差和抖动；
- 独立完整视频中的漏检和误检；
- C# ONNX 回放与真实 VRChat 流程。

模型来源、指标和哈希写入对应版本的模型卡，不在本文记录具体实验结果。验收通过后的模型按 [发布与许可](release.md) 和 [打包说明](../packaging/README.md) 生成发布物。
