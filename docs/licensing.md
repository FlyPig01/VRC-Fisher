# 许可证与发布边界

> 本文是项目的工程合规基线，不是法律意见。依赖、模型来源或发布方式变化时必须重新审计。

## 1. 许可证结构

VRC-Fisher 是多许可证项目，不应描述成“仓库内所有内容均为 MIT”。

| 范围 | 许可证或状态 | 可商用 | 关键条件 |
|---|---|---:|---|
| VRC-Fisher 原创应用、数据处理和通用工具代码 | MIT | 是 | 保留版权声明与 MIT 文本 |
| `training/` 完整训练子项目 | AGPL-3.0 | 是 | 分发或提供网络服务时履行 AGPL 源码义务 |
| Ultralytics、YOLO11 架构和官方基础权重 | 上游标注的 AGPL-3.0 | 是 | 遵守 Ultralytics 的 AGPL 条款与官方适用范围 |
| 官方训练产生的 `.pt` 和导出的 `.onnx` | 上游标注的 AGPL-3.0 | 是 | 不能标为 MIT；发布时附模型卡和许可证 |
| 其他第三方组件 | 各自许可证 | 依许可证 | 保留各组件要求的许可证与声明 |
| 录屏、抽帧、标注和生成数据集 | 默认私有，不授予公共许可 | 否 | 不提交仓库，不随源码或模型 Release 发布 |

根目录 `LICENSE` 只授予 VRC-Fisher 原创 MIT 内容。更具体的许可证优先：`training/LICENSE` 适用于完整训练子项目，`models/vX.Y.Z/MODEL_LICENSE.txt` 及模型 Release 中的相同文本适用于官方模型。

## 2. MIT 部分的使用范围

用户可以使用、复制、修改、合并、发布、再许可、销售 VRC-Fisher 的 MIT 原创代码，也可以将其用于闭源软件。用户必须保留版权声明和 MIT 许可证文本。MIT 不提供担保，也不授予模型、数据、商标、VRChat 平台或世界内容的额外权利。

只复用 C# 应用框架并换成来源合法、许可证兼容的独立模型时，不会因为 VRC-Fisher 自身的 MIT 代码产生公开源码义务。

## 3. Ultralytics 与 AGPL 范围

当前训练入口直接使用 `ultralytics.YOLO`，基础模型为 `yolo11n.pt`。项目按照 Ultralytics 当前官方许可说明采取保守合规策略：其训练代码、模型架构、官方基础权重、训练或微调后的检查点以及导出的 ONNX 均按上游标注的 AGPL-3.0 处理。上游 `pyproject.toml` 写 `AGPL-3.0`，PyPI 分类器写 AGPLv3+；项目不自行扩张或缩减该版本选择，而是随发布物提供上游完整许可证正文。

AGPL 允许个人、研究、非营利和商业使用，也允许收费。它不是“禁止商用”，其核心代价是对应的开源义务：

1. 向软件接收者提供完整对应源码，包括必要的修改、配置、构建和安装脚本。
2. 修改或组合的覆盖作品需要按 AGPL 提供相应权利，不得用额外 EULA 禁止 AGPL 已允许的修改和再分发。
3. 保留版权、许可证、无担保声明，并明确标出对上游的修改。
4. 如果修改后的 AGPL 程序通过网络与用户交互，应向这些网络用户提供取得对应源码的明确入口。
5. 分发二进制或模型时，应通过同一下载位置或其他合规方式提供对应源码，不得只发布不可构建的残缺代码。

Ultralytics 官方还主张：使用其代码、模型架构、训练流水线或训练/微调模型的更大项目，需要完整公开或取得 Enterprise 授权。VRC-Fisher 不购买 Enterprise 授权，因此官方发布采用公开源码路线。将 `.pt` 转换成 `.onnx`，或把模型改成独立 GitHub Release 下载，都不会自动清除上游许可。

官方说明：

- https://www.ultralytics.com/license
- https://www.ultralytics.com/legal/agpl-3-0-software-license

## 4. 仓库与发布物

仓库必须包含：

```text
LICENSE                    VRC-Fisher 原创代码的 MIT
THIRD_PARTY_NOTICES.md     第三方组件、模型和数据边界
training/LICENSE           完整 AGPL-3.0 文本
models/vX.Y.Z/             已验收 PT、ONNX、模型卡、模型许可证和源码清单
```

应用 Setup 必须携带 `LICENSE`、`THIRD_PARTY_NOTICES.md` 和 `licenses/AGPL-3.0.txt`。每个 `models/vX.Y.Z/` 必须携带：

```text
checkpoints/locator.pt
checkpoints/minigame.pt
runtime/locator.onnx
runtime/minigame.onnx
source-manifest.json
MODEL_CARD.md
MODEL_LICENSE.txt
```

模型 Release 是软件运行时下载子集，必须携带：

```text
locator.onnx
minigame.onnx
model-manifest.json
MODEL_CARD.md
MODEL_LICENSE.txt
```

模型卡至少记录基础权重、Ultralytics 版本、训练数据来源摘要、类别、数据划分、指标、已知限制、两个 `.pt` 与两个 ONNX 的大小、SHA-256 和许可证。没有完成模型卡时，模型构建脚本必须拒绝生成仓库模型目录和 Release。

模型清单使用 schema v2，将两个 ONNX 列入 `models`，将 `MODEL_CARD.md` 和 `MODEL_LICENSE.txt` 列入 `documentation`。软件内下载会对四个文件逐一校验大小和 SHA-256，再原子安装到用户选择的 `<安装目录>/models/`；缺少或损坏任一侧车文件时模型不可用。删除模型时也一并删除这两份文件，避免留下与实际模型版本不一致的许可记录。

## 5. 数据集策略

AGPL 的程序源码义务不等于必须公开原始训练截图。项目默认不公开录屏、抽帧、标注、审核图和生成数据集，原因包括 VRChat 世界美术、头像、用户名、字体和其他第三方内容的版权或隐私风险。

模型卡只发布不含个人信息的统计和来源说明。未来若公开数据集，必须先清理个人信息、确认素材再授权权利，并为数据集单独选择许可证；代码采用 MIT 不会让数据自动成为 MIT。

## 6. 独立实现约束

项目可以借鉴“全屏目标检测、状态机和反馈控制完成钓鱼”这类抽象思路，但不使用 `vrc-auto-fish` 的代码、权重、截图、标签、模板、界面素材、文档或独特实现。没有许可证的公开仓库默认保留全部版权，能够浏览或 fork 不等于能够复制和再发布。

## 7. 发布前检查

1. 锁定并审计实际依赖版本、传递依赖和原生 DLL。
2. 检查 Setup 包含三份法律文件，且不包含 `.pt`、数据集或录屏。
3. 检查 `models/vX.Y.Z/` 包含两个 `.pt`、两个 ONNX、模型卡、AGPL 文本和源码清单，并已提交到对应标签。
4. 检查模型 Release 包含模型卡、AGPL 文本、运行时清单和两个与仓库一致的 ONNX。
5. 检查 GitHub Release 对应标签同时提供完整源代码和模型权重。
6. README 和 Release 说明不得把模型或全部组合产品描述成 MIT。
7. 依赖升级、训练框架更换或数据公开前重新审计。

当前 Inno Setup 6.7.3 的编译器横幅显示 `Non-commercial use only`，但随安装文件提供的 `license.txt` 明确允许包括商业应用在内的任何用途。其官方商业许可 FAQ 进一步说明：商业用户被请求购买许可证，但并非严格要求。项目不购买该许可证也不等于禁止商业发布；升级 Inno Setup 时仍必须重新核对当时条款，并在 Setup 中保留实际版本的许可证文本。
