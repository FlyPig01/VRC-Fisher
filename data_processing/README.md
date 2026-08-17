# Data Processing

本目录负责把完整屏幕录屏转换为人工审核的全屏 YOLO 标注，并生成 locator 与 minigame 数据集。模型类别和训练契约见 [视觉与训练](../docs/vision-and-training.md)。

录屏、图片、标签和数据集默认只保存在本机，不提交 Git。

## 1. 环境

需要 Python 3.11 和 uv。从 `data_processing/` 执行：

```powershell
$Python311 = py -3.11 -c "import sys; print(sys.executable)"
uv sync --locked --python $Python311 --extra dev
uv run --offline pytest -q
```

环境位于 `data_processing/.venv/`。预标注会调用现有 `training/.venv/` 执行模型推理，不在本目录重复安装 PyTorch。

## 2. 目录

| 路径 | 职责 |
|---|---|
| `input/recordings/` | 原始完整屏幕录屏 |
| `input/annotations/<录屏名>/` | 最终人工审核的全屏 YOLO TXT |
| `work/frames/<录屏名>/` | 抽帧图片，可重建 |
| `work/annotations/<录屏名>/prelabels/` | 模型预标注，只读基线 |
| `work/annotations/<录屏名>/labels/` | 本地标注器草稿 |
| `work/annotations/<录屏名>/review.json` | 人工审核状态 |
| `work/annotations/<录屏名>/mapping.json` | 视频、帧和预标注来源 |
| `output/locator/` | locator 原始数据 |
| `output/minigame/` | minigame 裁剪数据 |
| `output/review/` | 带框审核图，不参与训练 |
| `configs/` | 可提交配置，不写本机绝对路径 |

图片和最终标签必须使用相同录屏名与文件名：

```text
work/frames/<录屏名>/frame-00000010.jpg
input/annotations/<录屏名>/frame-00000010.txt
```

## 3. 标签

| ID | 类别 | 框选对象 |
|---:|---|---|
| 0 | `bite_indicator` | 咬钩感叹号本身 |
| 1 | `minigame_panel` | 包含滑块和目标的主面板 |
| 2 | `catch_zone` | 鼠标控制的捕获区域 |
| 3 | `moving_target` | 鱼、齿轮或其他移动目标 |

人工只标全屏图片：

- 感叹号画面通常只标 `bite_indicator`；
- 小游戏画面必须标 `minigame_panel`、`catch_zone` 和 `moving_target`；
- 不标轨道、进度条、成功、失败和装饰元素；
- minigame 局部图片由工具自动裁剪，不维护第二套人工标签。

## 4. 推荐流程

### 4.1 生成预标注批次

录屏建议放在 `input/recordings/`，也可以从任意路径选择：

```powershell
uv run vrc-prelabel
```

或：

```powershell
uv run vrc-prelabel --input "input/recordings/<录屏名>.mp4" --interval 0.5
```

命令完成抽帧、重复图片去除和四类预标注，输出到 `work/frames/` 与 `work/annotations/`。输入视频不会被复制或移动。

目标批次已存在时默认停止。只有确认要删除该批次全部草稿和审核进度时才使用 `--replace`。

### 4.2 人工审核

```powershell
uv run vrc-annotate --recording "<录屏名>"
```

标注器只监听 `127.0.0.1:8765`，不会上传图片。预标注只是草稿，必须人工：

- 补充漏框；
- 删除误框；
- 修正边界和类别；
- 明确确认每一张正样本或负样本。

拖框后自动保存草稿；只有“确认并下一张”或“确认负样本”才记为已审核。关闭页面后可以用同一命令继续。

### 4.3 提交最终标签

全部帧审核完成后执行：

```powershell
uv run vrc-commit-annotations --recording "<录屏名>"
```

命令重新检查坐标、类别和小游戏三类组合，再事务性写入：

```text
input/annotations/<录屏名>/*.txt
```

已有最终标签时默认停止；只有确认替换整段录屏时才使用 `--replace`。

### 4.4 生成和审核数据

```powershell
uv run vrc-audit-annotations
uv run vrc-build-locator
uv run vrc-build-minigame --padding 0 --negative-ratio 0.2
uv run vrc-build-review
```

必须人工检查：

```text
output/review/locator/
output/review/minigame/
```

minigame 构建会保留全部正样本，并按配置从审核负样本中选择局部困难负样本。

### 4.5 划分训练集

至少需要两段独立且已审核的录屏：

```powershell
uv run vrc-split-recordings --input output/locator --output ../training/datasets/locator
uv run vrc-split-recordings --input output/minigame --output ../training/datasets/minigame
```

工具按录屏生成 `train`、`val` 和 `split.json`。同一录屏不得跨集合。完整测试视频放在 `training/test/videos/`，不进入 `data.yaml`。

## 5. 无预标注流程

不使用模型时：

1. 使用 `vrc-extract-frames --input <video> --interval <seconds>` 抽帧；
2. 删除无用图片；
3. 使用任意支持 YOLO 的工具标注全屏图片；
4. 将同名 TXT 放入 `input/annotations/<录屏名>/`；
5. 从“生成和审核数据”继续。

需要保留为负样本的图片必须有同名空 TXT。没有 TXT 的图片表示尚未审核，不进入数据集。

## 6. 文件状态

| 图片 | 同名 TXT | 含义 |
|---|---|---|
| 存在 | 非空 | 已审核正样本 |
| 存在 | 空文件 | 已审核负样本 |
| 存在 | 不存在 | 未审核，不进入数据集 |
| 不存在 | 存在 | 无效残留标签 |

空预标注不等于人工确认负样本；只有标注器确认或最终空 TXT 才算负样本。

## 7. 清理与停止条件

长期保留：

- 原始录屏；
- `input/annotations/` 最终标签；
- 已审核且仍需训练的 `output/` 或 `training/datasets/`。

最终标签和输出确认正确后，可以删除对应 `work/frames/` 与 `work/annotations/`，它们可由原视频重建。

以下情况必须停止：

- 图片与标签不匹配；
- YOLO 坐标或类别非法；
- 小游戏三类组合错误；
- 只有一段录屏却尝试生成正式 `train/val`；
- 审核未完成或同一录屏跨集合。
