# 发布与许可

本文定义版本、发布物、安装目录和许可证边界。具体构建命令以 [`packaging/README.md`](../packaging/README.md) 为准。

## 1. 版本与发布物

应用和模型独立版本化：

```text
app-vX.Y.Z       VRC-Fisher-Setup-x64.exe
models-vX.Y.Z    locator.onnx、minigame.onnx、运行时清单、模型卡和许可证
```

应用版本变化时发布新的 `app-v*`。只有模型变化时只发布新的 `models-v*`，无需重新发布 Setup。软件选择满足运行时 API 的最新兼容模型版本。

## 2. 安装目录

Setup 允许选择界面语言和任意可写安装目录。软件、模型、配置、下载暂存和日志均位于该目录，不依赖 Setup 文件所在位置，也不固定在 C 盘。

Windows 仍会保存卸载登记、快捷方式和系统安全记录；这些属于系统级元数据，不是软件运行数据。

## 3. 模型更新

Setup 不包含 ONNX。软件模型页负责下载、更新、删除和重新下载模型：

- 清单使用语义版本判断兼容最新版；
- 只扫描非 Draft、非 Pre-release 的 `models-v*` Release；
- 文件按大小和 SHA-256 校验；
- 下载完成后原子替换旧模型；
- 断点只在版本和清单未变化时续传；
- 缺失显示下载，有新版显示更新，当前最新版显示删除。

`Auto / GPU / CPU` 使用同一套 ONNX。

## 4. 发布门禁

应用发布必须确认：

- Debug/Release 自动测试通过；
- 20 种语言资源键一致；
- Setup 不包含 ONNX、PDB 或 DirectML 调试层；
- 非 C 盘安装、覆盖安装和卸载通过；
- 模型下载、更新、校验和回滚通过；
- 对应真实 VRChat 验收项已经完成。

模型发布必须从已提交的 `models/vX.Y.Z/` 构建，Release 文件必须与源码中的运行时模型一致。

## 5. 许可证边界

| 内容 | 许可证或状态 |
|---|---|
| 原创 C#、数据处理和通用工具代码 | MIT |
| `training/`、Ultralytics 训练链及其衍生模型 | 按上游 AGPL-3.0 |
| 第三方库 | 各自许可证，见 `THIRD_PARTY_NOTICES.md` |
| 录屏、抽帧、标注和数据集 | 默认私有，不随代码或 Release 发布 |

模型必须随模型卡和完整许可证发布。开源许可证不代表 VRChat 或目标世界允许自动化操作。
