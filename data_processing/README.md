# Data Processing

此目录将完整屏幕录屏转换为可审核的全屏帧，并从一套全屏标注生成 locator 与 minigame 两个数据集。所有视频、图片、标签和生成结果只保存在本机，不提交 Git。

本目录的原创工具代码采用根目录 MIT License；录屏、抽帧、标注、审核图和生成数据集不在该 MIT 授权范围内，默认保持私有且不授予公共数据许可。公开任何数据前必须另行处理 VRChat 世界素材、头像、用户名和其他第三方内容的版权与隐私问题。

本目录使用四类标签。审核状态直接由抽帧图片和同名 YOLO `.txt` 表达，不维护额外清单。

## 准备环境

需要 Python 3.11 和 uv：

```powershell
$Repo = "C:\path\to\VRC-Fisher"
Set-Location (Join-Path $Repo "data_processing")
uv sync --locked --extra dev
uv run --offline pytest -q
```

该环境只用于离线处理，不进入 C# 软件或 Setup。首次部署、Python 解释器选择和三套环境的边界见 [开发环境部署](../docs/development-setup.md)。

## 目录

| 路径 | 内容 |
|---|---|
| `input/recordings/` | 原始完整屏幕录屏，例如 `.mp4` |
| `work/frames/` | 工具抽取的完整屏幕 JPG |
| `work/annotations/<录屏名>/prelabels/` | 模型生成的原生全屏 YOLO TXT，只读基线 |
| `work/annotations/<录屏名>/labels/` | 本地页面自动保存的人工草稿 YOLO TXT |
| `work/annotations/<录屏名>/review.json` | 明确完成审核的帧列表 |
| `work/annotations/<录屏名>/mapping.json` | 帧、视频、模型哈希和预标注参数 |
| `input/annotations/<录屏名>/` | 人工审核的全屏 YOLO `.txt` 标签 |
| `output/locator/` | 由标签生成的全屏原始数据集 |
| `output/minigame/` | 按 UI 框自动裁剪的局部原始数据集 |
| `output/review/` | 绘制类别框的人工审核图，不参与训练 |
| `configs/` | 可提交的处理配置；不能写本机绝对路径 |

帧和标签必须按录屏名使用相同相对路径：

```text
work/frames/<录屏名>/frame-00000010.jpg
input/annotations/<录屏名>/frame-00000010.txt
```

## 本地预标注与人工审核

所有命令都从 `E:\MyTools\VRC-Fisher\data_processing` 执行。`data_processing/.venv` 负责抽帧、校验和格式转换；模型推理由命令自动调用现有 `training/.venv`，不在 `data_processing` 中重复安装 PyTorch。

### 1. 选择新视频

录屏可以位于任意目录。不传 `--input` 时会打开 Windows 文件选择器：

```powershell
uv run vrc-prelabel
```

也可以在命令中传入任意相对或绝对路径：

```powershell
uv run vrc-prelabel --input "D:\Recordings\<录屏名>.mp4"
```

需要与项目一起保存的原始录屏仍建议放在：

```text
input/recordings/<录屏名>.mp4
```

输入视频不会被复制或移动。默认抽帧位于 `work/frames/`，预标注和人工草稿位于 `work/annotations/`；需要切换工作目录时使用 `--frames-root` 和 `--batches-root`。

### 2. 生成原生 YOLO 预标注

确认两个首轮权重存在：

```text
../training/runs/locator-best-init/weights/best.pt
../training/runs/minigame-best-init/weights/best.pt
```

然后执行：

```powershell
Set-Location E:\MyTools\VRC-Fisher\data_processing
uv run vrc-prelabel `
  --input "input/recordings/<录屏名>.mp4" `
  --interval 0.5
```

命令同时完成稀疏抽帧、CUDA 预标注和完全相同图片去重。预标注直接采用项目四类全屏 YOLO TXT，不生成平台 JSON、不复制第二套图片，也不打 ZIP。默认阈值为：`bite_indicator=0.15`、`minigame_panel=0.20`、`catch_zone=0.20`、`moving_target=0.05`；`minigame_panel` 预标注裁剪不再额外外扩。阈值偏低是为了供人工纠错，不能作为软件运行阈值。

输出固定为：

```text
work/frames/<录屏名>/*.jpg
work/annotations/<录屏名>/prelabels/*.txt
work/annotations/<录屏名>/labels/
work/annotations/<录屏名>/review.json
work/annotations/<录屏名>/mapping.json
```

空的预标注 TXT 只表示模型没有检出，不代表人工确认的负样本。目标目录已存在时默认停止；只有确认要丢弃该批次全部草稿和审核进度时才使用 `--replace`。

### 3. 打开本地标注器

```powershell
uv run vrc-annotate --recording "<录屏名>"
```

命令只监听 `127.0.0.1:8765` 并打开浏览器，图片和标签不会上传网络。页面支持新增、边线拖动、四角缩放、节点列表精确选框、改类别、删除框、滚轮缩放图片、中键或空格拖动画布、拖动图片进度条跳转、方向键或 `A/D` 切图，以及 `1` 到 `4` 选择类别。画布上的框不显示类别文字，点击框内透明区域会开始新建框，不会选中已有框。

固定标签为：

```text
bite_indicator
minigame_panel
catch_zone
moving_target
```

预标注只是草稿。必须补漏框、删除误框并调整边界，尤其检查绿色鱼等 `moving_target`。小游戏画面必须同时有且仅有一个 `minigame_panel`、一个 `catch_zone` 和一个 `moving_target`。

拖框结束后页面自动保存到 `labels/`，但只有点击“确认并下一张”才算人工审核；空画面必须点击“确认负样本”。已审核帧再次修改后会自动回到待审核。点击“恢复预标注”会删除该帧人工草稿并恢复模型初始结果。

关闭页面不会丢失进度。重新执行相同命令即可继续。终端按 `Ctrl+C` 只停止本地服务，不删除数据。

### 4. 提交最终标签

页面显示全部帧审核完成后，停止本地服务并执行：

```powershell
uv run vrc-commit-annotations --recording "<录屏名>"
```

命令要求每一帧都被明确审核，并再次检查 YOLO 坐标、重复类别和小游戏三类组合，然后事务性生成：

```text
input/annotations/<录屏名>/*.txt
```

这是项目真正使用的最终全屏 YOLO 标签。已有 TXT 时默认停止；只有确认要替换整段录屏的全部标签时才使用 `--replace`。提交失败不会写入半套标签。

### 5. 后续数据生成

导入成功后依次运行：

```powershell
uv run vrc-audit-annotations
uv run vrc-build-locator
uv run vrc-build-minigame --padding 0 --negative-ratio 0.2
uv run vrc-build-review
```

人工检查 `output/review/` 后才能按录屏划分到训练目录。minigame 保留全部正样本，并从空标签帧中均匀抽取最多正样本数 20% 的局部困难负样本；负样本使用同录屏最近的面板位置裁剪，不直接复制全屏图。原始视频和最终标签长期保留；确认最终标签和 `output/` 正确后，`work/frames/<录屏名>/` 与 `work/annotations/<录屏名>/` 可以删除并从原视频重建。

## 纯手工流程

不使用本地模型预标注时，执行以下流程：

1. 将录屏放入 `input/recordings/`。
2. 抽取完整屏幕帧：

   ```powershell
   uv run vrc-extract-frames --interval 0.25
   ```

   只处理一段指定录屏时，直接将文件路径传给 `--input`：

   ```powershell
   uv run vrc-extract-frames --input input/recordings/20260812-2035-32.2147786.mp4 --interval 0.25
   ```

3. 筛选图片并用 YOLO 格式标注，将标签保存到 `input/annotations/` 的对应目录。无用图片直接删除；确认没有目标但希望作为负样本保留的图片，需要创建同名空 TXT。
4. 审计标注：

   ```powershell
   uv run vrc-audit-annotations
   ```

5. 审计无错误后生成两份原始数据：

   ```powershell
   uv run vrc-build-locator
   uv run vrc-build-minigame --padding 0 --negative-ratio 0.2
   ```

6. 生成审核图并人工检查所有类别框和负样本：

   ```powershell
   uv run vrc-build-review
   ```

   审核图位于 `output/review/locator/` 和 `output/review/minigame/`。红色是类别 0，绿色是类别 1；空标签图片显示 `NEGATIVE`。审核图不进入训练集。

7. 至少有两段独立且已标注的录屏后，按录屏划分并复制到训练目录：

   ```powershell
   uv run vrc-split-recordings --input output/locator --output ../training/datasets/locator
   uv run vrc-split-recordings --input output/minigame --output ../training/datasets/minigame
   ```

   划分命令同时写入 `split.json`，用于审核每段录屏属于 `train` 或 `val`。同一录屏绝不能跨集合。

   工具只创建 `images/{train,val}/` 和 `labels/{train,val}/`。完整大屏测试视频放在 `training/test/videos/`，不需要 TXT 标签，也不会写入 `data.yaml`。

PowerShell 中同样从 `data_processing/` 目录执行这些命令。工具的相对默认路径依赖当前目录。

## 标注规则

| ID | 类别 | 框选对象 |
|---:|---|---|
| 0 | `bite_indicator` | 咬钩时出现的感叹号，只框提示本身 |
| 1 | `minigame_panel` | 包住 `catch_zone` 和 `moving_target` 的主控制面板；不要求包含左侧进度条 |
| 2 | `catch_zone` | 由鼠标控制、需要覆盖目标的捕获区域 |
| 3 | `moving_target` | 需要保持在捕获区域内的鱼、齿轮或其他物件 |

只人工标注完整屏幕，不维护第二套局部标注：

- 感叹号画面通常只标 `bite_indicator`；
- 小游戏画面标 `minigame_panel`、`catch_zone` 和 `moving_target`；
- 轨道、进度条、成功和失败不标；
- `minigame` 图片由 `minigame_panel` 自动裁剪，类别 2、3 分别换算为局部模型的类别 0、1。

图片是否进入数据集由文件状态决定：

| 图片 | 同名 `.txt` | 结果 |
|---|---|---|
| 存在 | 非空 | 正样本，进入数据集 |
| 存在 | 空文件 | 负样本，进入 locator 数据集 |
| 存在 | 不存在 | 尚未标注，不进入数据集 |
| 不存在 | 任意 | 不参与处理；残留 TXT 会被忽略 |

不要求给每张抽帧图片做标注。无用帧直接从 `work/frames/` 删除；需要保留为负样本的帧不能删除。

## 停止条件

没有带同名 TXT 的图片时，构建命令必须失败；类别组合错误或坐标越界时，审计也必须失败。只有一段录屏时划分工具会拒绝制造 `train/val`；此时可以检查抽帧和标注流程，但不能开始正式训练或报告验证结果。

完整的数据契约见 [视觉、数据与训练](../docs/vision-and-training.md)。
