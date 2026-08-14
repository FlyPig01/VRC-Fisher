# 视觉、数据与训练

## 1. 为什么是双模型

UI 会在完整屏幕内移动和缩放，小游戏中的目标外观也会变化。固定坐标、颜色阈值和模板匹配无法覆盖这些变化，因此运行时使用两个目标检测模型：

| 模型 | 输入 | 职责 |
|---|---|---|
| `locator.onnx` | 完整显示器缩放到 `960 x 960` | 找咬钩感叹号和整套小游戏 UI |
| `minigame.onnx` | 从原始帧按 UI 框裁出并缩放到 `640 x 640` | 找捕获区域和需要追踪的移动目标 |

第一阶段先在整屏定位 UI，第二阶段再回到原始帧裁剪，因此不会把全屏缩放后丢失的局部细节交给第二模型。OpenCV 不参与识别，也不存在识别失败后改用模板匹配的路径。

## 2. 模型与类别

训练框架固定为 Ultralytics YOLO11，首个基线使用 `yolo11n.pt`。`n` 是 YOLO11 中体积和计算量最低的常规检测规格，适合两个小模型和实时目标；只有验证结果证明容量不足时，才评估更大规格。

许可基线同样固定：完整 `training/` 子项目以及通过该 Ultralytics 训练链产生的官方 `.pt` 和 `.onnx` 按上游标注的 AGPL-3.0 发布，不属于根目录 MIT。AGPL 允许商业使用，但分发或网络部署覆盖作品时需要履行对应源码等义务；改变文件格式或把模型做成独立下载不改变这一处理。原始录屏、抽帧和标注默认私有，不因代码或模型许可证而公开。

人工全屏标签固定为四类：

| ID | 类别 | locator | minigame |
|---:|---|:---:|:---:|
| 0 | `bite_indicator` | 转换为 0 | 否 |
| 1 | `minigame_panel` | 转换为 1 | 裁剪依据 |
| 2 | `catch_zone` | 否 | 转换为 0 |
| 3 | `moving_target` | 否 | 转换为 1 |

标注对象：

- `bite_indicator`：咬钩时出现的感叹号，只框提示本身；
- `minigame_panel`：主控制面板的紧致外接框，必须包住 `catch_zone` 与 `moving_target`；左侧进度条不参与控制，不要求纳入此框；
- `catch_zone`：由鼠标按住和松开控制、需要覆盖目标的区域；
- `moving_target`：需要保持在捕获区域内的物件。

鱼、齿轮等所有需要跟随的物件统一标为 `moving_target`，除非后续数据证明它们需要不同控制策略。成功、失败、轨道和进度条不参与第一版识别，也不得为了“可能以后有用”而增加标注负担。

当前数据缩放到 `640` 时，最小 `bite_indicator` 约为 `4.4 x 14.8` 像素，存在明显小目标风险；缩放到 `960` 后约为 `6.6 x 22.2` 像素。感叹号本身有持续放大缩小动画，运行时只需在一个较大动画相位捕获，但不能把 locator 频率降到可能长期错过动画的程度。因此 locator 基线固定为 `960`，minigame 的局部目标在 `640` 下已有足够像素，继续保持 `640`。

## 3. 只维护全屏训练源

人工只标注抽取出的完整屏幕图片，不维护第二套局部标注。数据流如下：

```text
完整屏幕录屏
  -> 抽取完整屏幕帧
  -> 人工标注四类目标
  -> 标注审计
  -> locator 全屏数据集
  -> 按 minigame_panel 自动裁剪并换算 catch_zone / moving_target 标签
  -> minigame 局部数据集
  -> 按录屏划分 train / val
```

完整视频验收不进入训练数据：未标注的大屏视频放入 `training/test/videos/`，双模型输出 `training/test/results/` 的审核视频。

为减少人工绘框，已实现完全本地的预标注审核流程：在 `data_processing/` 运行 `vrc-prelabel`，由当前两个 `best.pt` 对新录屏稀疏抽帧并直接生成四类全屏 YOLO TXT；`vrc-annotate` 只监听 `127.0.0.1`，浏览器直接读取本机抽帧并保存人工草稿。模型空结果不等于负样本，只有页面明确确认的帧才进入审核状态；全部完成后由 `vrc-commit-annotations` 再次审计并事务写入最终标签。自动预标注不属于真值，未经人工审核不得进入数据集。

训练集中的局部图是数据生成产物。运行时则由 `locator.onnx` 的预测框执行同类裁剪，所以训练时必须加入合理边距和位置扰动，验证对定位误差的容忍度。

## 4. 目录与文件流向

| 路径 | 放什么 | Git |
|---|---|---|
| `data_processing/input/recordings/` | 原始完整屏幕录屏 | 忽略 |
| `data_processing/work/frames/` | 从录屏抽取的完整屏幕 JPG | 忽略 |
| `data_processing/work/annotations/` | 原生 YOLO 预标注、人工草稿、审核状态和本地映射 | 忽略 |
| `data_processing/input/annotations/<录屏名>/` | 人工审核的全屏 YOLO `.txt` | 忽略 |
| `data_processing/output/locator/` | 生成的全屏原始数据集 | 忽略 |
| `data_processing/output/minigame/` | 自动裁剪的局部原始数据集 | 忽略 |
| `data_processing/output/review/` | 绘制类别框的人工审核图，不参与训练 | 忽略 |
| `training/datasets/locator/` | 按录屏划分后的 locator 数据集 | 图片和标签忽略 |
| `training/datasets/minigame/` | 按录屏划分后的 minigame 数据集 | 图片和标签忽略 |
| `training/runs/` | Ultralytics 训练运行目录 | 忽略 |
| `training/weights/` | 手工保留的 `.pt` 检查点 | 忽略 |
| `training/exports/` | 导出的两个 ONNX | 忽略 |
| `app/models/` | C# 开发回放使用的已审核 ONNX | 模型忽略 |
| `models/vX.Y.Z/` | 正式开源模型：两个 `.pt`、两个 ONNX、模型卡、许可证和清单 | 提交 |

抽帧与标签按录屏名称保持同一层级：

```text
work/frames/<录屏名>/frame-00000010.jpg
input/annotations/<录屏名>/frame-00000010.txt
```

非空 TXT 表示正样本，空 TXT 表示负样本；二者都可进入 locator 数据集。图片没有同名 TXT 表示尚未标注，不进入数据集；删除图片表示不采用。标注 TXT 没有对应缓存图片时也不参与处理，重新从原始录屏抽取同名帧后才会恢复对应关系。

数据生成后必须检查 `output/review/locator/` 和 `output/review/minigame/`。至少两段独立录屏完成标注后，划分工具按整段录屏生成 `train/val`，并将归属写入两个训练数据集的 `split.json`。训练前预检同时核对类别、图片与标签、坐标、录屏隔离和两个集合的类别覆盖；预检不会加载或下载模型。

完整视频测试使用 `training` 中的 `vrc-review-video`：每帧先定位全屏 `minigame_panel`，再裁剪局部图检测小游戏目标，最后映射框回原始大屏并写入 `training/test/results/`。视频无需标注，不进入 `data.yaml`，只能人工检查，不能计算检测指标。Python 审核默认使用 `.pt`，C# 发布程序使用导出的 ONNX。

## 5. 数据停止条件

数据是当前项目的硬门槛。出现以下任一情况立即停止构建或训练：

- 没有人工审核标签；
- 标注越界、类别错误，或 `catch_zone` / `moving_target` 所在帧缺少 `minigame_panel`；
- 只有一段录屏，却准备报告验证结果；
- 同一录屏的相邻帧被随机拆到训练集和验证集；
- 某个关键状态或 UI 尺寸没有样本；
- 没有覆盖感叹号位于屏幕内不同位置，以及完全看不到感叹号的等待画面；
- 自动裁剪后目标被截断，且边距或标签换算尚未修正。

至少两段独立录屏才能建立非空 `train` 与 `val`；最终验收应使用未参与训练和调参的完整视频，放在 `training/test/videos/`，不制作带标签的 YOLO `test` 集。只有一段录屏时允许做管线检查或过拟合实验，但结果不能称为泛化性能。

## 6. 训练与导出

训练默认配置在 `training/configs/default.toml`。当前开发基线在 RTX 4060 Laptop GPU 的 `cuda:0` 上运行，使用项目内 PyTorch `2.13.0+cu130`；不需要 Miniconda 或全局 CUDA Toolkit。CUDA 训练与用户端 DirectML 无关。

当前固定参数为：locator `imgsz=960`、`batch=4`，minigame `imgsz=640`、`batch=8`；两者均为 `epochs=100`、`patience=20`、`workers=4`、`seed=42`、`device=0`，首轮基础权重为 `yolo11n.pt`。训练入口显式传入 `pretrained=true` 和 `plots=true`。未显式覆盖的参数由锁定的 Ultralytics 8.4.118 提供；当前小数据规模下 `optimizer=auto` 选择 AdamW。2026-08-14 已完成首轮训练，但 `moving_target` Recall 只有 0.392，模型仅可用于人工复核的预标注，不可发布。

截至 2026-08-14，数据结构预检结果为：locator 551 张图，其中 269 张正样本、282 张负样本，共有 97 个 `bite_indicator` 和 172 个 `minigame_panel` 框；minigame 172 张图，均含 `catch_zone` 和 `moving_target`。新增的 80 帧感叹号动画录屏完整进入 train，不与 val 混帧；locator train 现有 91 个感叹号框，独立 val 有 6 个。格式预检通过不代表数据质量和验证覆盖充分，开始训练仍需人工批准。

`.pt` 用于开发时继续训练和保存优化器相关实验；最终 C# 软件只加载 ONNX。两个模型目标契约固定为 opset 17、静态输入、无内置 NMS，locator 输入为 `960 x 960`，minigame 输入为 `640 x 640`。C# 从每个 ONNX 的静态输入元数据读取各自尺寸，不允许一个全局尺寸覆盖两个模型。预处理、NMS 和坐标映射由 C# 实现并以独立张量测试覆盖。locator 输出两类，minigame 输出两类。Python 数据工具已同步为四类并由自动化测试覆盖；C# 类别映射仍是旧八类实现，必须在加载新模型前同步。当前测试不证明识别准确率。

CPU-only、DirectML 和 CUDA Provider 可以使用同一 ONNX 文件。Provider 改变执行后端，不改变权重，因此不需要重新训练；FP16、INT8、TensorRT 等专项产物需要另行转换和验证，但也不是重新训练。

### `.pt` 与 ONNX 的边界

`.pt` 是 PyTorch/Ultralytics 训练检查点，适合继续训练、查看实验和回退到某个 epoch，但运行它通常需要 Python、PyTorch 及对应依赖。把 `.pt` 发给用户会扩大安装体积，并把训练框架带入正式软件。

ONNX 是导出后的推理图，C# 可以直接通过 ONNX Runtime 加载，同一文件可交给 CPU-only 或 DirectML Provider。导出和图优化通常能减少运行依赖，有利于启动和部署，但不能保证每次都比 `.pt` 更快或更小；模型文件大小、预处理、NMS 和 Provider 执行时间必须在正式导出后实测。

正式模型首先固化到源码仓库的 `models/vX.Y.Z/`：保留两个选定的 `.pt` 作为可修改权重，保留两个 ONNX 作为运行时模型，并附带已填写的 `MODEL_CARD.md`、完整 `MODEL_LICENSE.txt` 和覆盖所有文件的 `source-manifest.json`。模型卡记录基础权重、训练版本、私有数据集的非识别性统计、划分、指标、限制、文件大小和 SHA-256；缺少这些信息不得提交模型或创建 Release。模型 Release 只从这个仓库目录提取两个 ONNX、模型卡和许可证，不能从另一个本地来源临时拼装。

因此源码仓库同时公开 `.pt` 和 ONNX：开发者使用 `.pt` 检查或继续训练，C# 软件只使用 ONNX。这是开发与运行时边界，不是对准确率的承诺。

具体命令见 [数据处理 README](../data_processing/README.md) 和 [训练 README](../training/README.md)。

## 7. 验收指标

不能只看 Ultralytics 的 mAP。每个模型至少记录：

- 按状态和尺寸分组的漏检与误检；
- `bite_indicator` 在不同屏幕位置的漏检，以及屏外提示时计时兜底的行为；
- `minigame_panel` 的定位偏差及其对局部裁剪的影响；
- `catch_zone` 上下边界与 `moving_target` 中心位置误差；
- 连续录屏中的抖动、短暂丢失和错误状态切换；
- 完整 C# 回放的端到端帧龄、状态机恢复与输入决策。

发布前使用未参与训练和阈值调整的录屏做最终验证，再进行“仅观察”现场测试。没有正式模型前，任何性能或准确率数字都只能是目标，不能写成结果。
