# 开发文档

本目录只保存当前有效的跨模块设计。历史实验、旧方案和已处理缺陷位于 [`archive/`](archive/README.md)，不得作为现行实现依据。

## 文档职责

| 文档 | 唯一职责 |
|---|---|
| [开发环境](development.md) | 各工作区的开发依赖、环境隔离和验证入口 |
| [架构与运行](architecture.md) | C# 分层、实时管线、状态、安全、持久化和日志边界 |
| [视觉与训练](vision-and-training.md) | 双模型、类别、数据集和模型验收契约 |
| [小游戏控制](minigame-control.md) | `catch_zone` 控制算法和控制参数 |
| [发布与许可](release.md) | 版本、发布物、安装目录、模型更新和许可证边界 |
| [缺陷记录](bug.md) | 当前尚未关闭的缺陷与验收条件 |

具体操作手册不在本目录重复：

- 录屏、标注和数据集生成以 [`data_processing/README.md`](../data_processing/README.md) 为准。
- 训练、评估和导出以 [`training/README.md`](../training/README.md) 为准。
- C# 工程入口以 [`app/README.md`](../app/README.md) 为准。
- 安装包和模型包构建以 [`packaging/README.md`](../packaging/README.md) 为准。
- 最终用户操作以 [`USER_GUIDE.md`](../USER_GUIDE.md) 为准。

## 维护规则

1. 设计文档只描述当前行为，不记录调试过程。
2. 实验数据进入 `archive/` 或 `training/reports/`。
3. 已关闭缺陷移入缺陷历史，`bug.md` 只保留未关闭项。
4. 同一参数只在负责该参数的文档中定义，其他位置使用链接。
5. 程序、命令或目录变化时，同一提交内更新对应职责文档。
