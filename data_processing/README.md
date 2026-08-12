# Data Processing

此目录将完整屏幕录屏转换为可审核的全屏帧，并从一套全屏标注生成 locator 与 minigame 两个数据集。所有视频、图片、标签和生成结果只保存在本机，不提交 Git。

## 准备环境

需要 Python 3.11 和 uv：

```powershell
Set-Location E:\MyTools\VRC-Fisher\data_processing
uv sync --extra dev
uv run pytest -q
```

该环境只用于离线处理，不进入 C# 软件或 Setup。

## 目录

| 路径 | 内容 |
|---|---|
| `input/recordings/` | 原始完整屏幕录屏，例如 `.mp4` |
| `work/frames/` | 工具抽取的完整屏幕 JPG 和 `manifest.jsonl` |
| `input/annotations/` | 人工审核的全屏 YOLO 标签 |
| `output/locator/` | 由标签生成的全屏原始数据集 |
| `output/minigame/` | 按 UI 框自动裁剪的局部原始数据集 |
| `configs/` | 可提交的处理配置；不能写本机绝对路径 |

帧和标签必须按录屏名使用相同相对路径：

```text
work/frames/<录屏名>/frame-00000010.jpg
input/annotations/<录屏名>/frame-00000010.txt
```

## 执行顺序

1. 将录屏放入 `input/recordings/`。
2. 抽取完整屏幕帧：

   ```powershell
   uv run vrc-extract-frames --interval 0.25
   ```

3. 用 YOLO 格式人工标注 `work/frames/` 中的图片，将标签保存到 `input/annotations/` 的对应目录。
4. 审计标注：

   ```powershell
   uv run vrc-audit-annotations
   ```

5. 审计无错误后生成两份原始数据：

   ```powershell
   uv run vrc-build-locator
   uv run vrc-build-minigame --padding 0.08
   ```

6. 至少有两段独立录屏后，按录屏划分并复制到训练目录：

   ```powershell
   uv run vrc-split-recordings --input output/locator --output ../training/datasets/locator
   uv run vrc-split-recordings --input output/minigame --output ../training/datasets/minigame
   ```

PowerShell 中从 `data_processing/` 目录执行这些命令。工具的相对默认路径依赖当前目录。

## 标注规则

| ID | 类别 |
|---:|---|
| 0 | `prompt` |
| 1 | `fishing_ui_group` |
| 2 | `success` |
| 3 | `failure` |
| 4 | `rail` |
| 5 | `control_bar` |
| 6 | `target` |
| 7 | `progress_bar` |

只人工标注完整屏幕。`minigame` 图片由 `fishing_ui_group` 自动裁剪，4-7 类标签由工具换算，不要维护第二份局部人工标注。

标签文件存在但内容为空，表示已审核的负样本；标签文件不存在，表示该帧尚未审核，不会进入数据集。

## 停止条件

没有人工标注时，构建命令会失败。审计报告任何错误时必须先修正，不能继续生成数据集。只有一段录屏时划分工具会拒绝制造 `train/val`；此时可以检查抽帧和标注流程，但不能开始正式训练或报告验证结果。

完整的数据契约见 [视觉、数据与训练](../docs/vision-and-training.md)。
