# 架构与运行

本文只描述正式 C# 软件的运行结构。视觉契约见 [视觉与训练](vision-and-training.md)，小游戏算法见 [小游戏控制](minigame-control.md)。

## 1. 运行边界

软件只处理前台 `VRChat.exe` 主窗口：

- Windows Graphics Capture 捕获 VRChat 窗口；
- ONNX Runtime 执行视觉模型；
- Win32 `SendInput` 提交鼠标左键；
- 不捕获全显示器、不选择任意进程、不读游戏内存、不修改游戏文件；
- 不使用 OpenCV、固定坐标或颜色阈值作为运行时识别方案。

## 2. 分层

```text
Desktop -> Infrastructure -> Application -> Core
```

| 层 | 职责 |
|---|---|
| `Core` | 状态机、检测契约、小游戏决策和平台抽象 |
| `Application` | 启停事务、配置、运行快照和错误状态 |
| `Infrastructure` | WGC、画面读回、ONNX、文件、模型下载和 SendInput |
| `Desktop` | WinUI 3 页面、本地化、覆盖层和依赖组装 |

页面不能直接调用 WGC、ONNX 或 Win32；捕获、推理和控制不运行在 UI 线程。

## 3. 实时管线

```text
WGC 最新帧
  -> locator.onnx
  -> 锁定 minigame_panel 并裁剪
  -> minigame.onnx
  -> Core 状态机与控制器
  -> SendInput
```

运行时只保留最新画面，旧帧在推理落后时丢弃。等待阶段主要运行 locator；小游戏阶段复用面板区域运行 minigame，并按需要重新定位面板。

运行时对 `bite_indicator` 使用独立置信度阈值 `0.60`，并继续要求最近 5 次识别至少命中 3 次；`minigame_panel`、`catch_zone` 和 `moving_target` 保持 `0.35`，避免降低小游戏组件召回率。

识别间隔由性能调度器根据已有推理耗时自动调整，不额外调用模型。小游戏当前允许约 `15–25 Hz`，精确规则属于性能调度实现，不是用户配置项。

## 4. 状态与安全

主流程：

```text
抛竿 -> 等待感叹号 -> 收钩 -> 小游戏 -> 收杆 -> 下一轮
```

小游戏结束后先保持左键释放并等待 `1 秒`，再点击收杆；收杆点击实际完成后再等待 `2 秒`，随后才允许下一次抛竿。两段时间分别从小游戏结束决定和收杆输入完成时开始计算。

主要状态为 `Idle`、`Casting`、`WaitingForBite`、`Hooking`、`Minigame`、`Reeling`、`Recovery`。

以下情况必须释放左键并停止或恢复：

- VRChat 失去前台、退出或最小化；
- 小游戏组件连续两次缺失、面板重定位或画面过期；
- 捕获、推理或输入失败；
- 软件收到停止请求；
- 小游戏控制计划超过动态反馈期限仍没有新坐标。

启动事务只有在模型、推理会话、WGC 和首帧都成功后才进入运行态。返回 VRChat 前台不会自动恢复。

## 5. 持久化

所有主动管理的数据位于用户选择的安装目录：

```text
program/    程序与语言资源
config/     用户设置、性能画像、小游戏动力参数
models/     已安装模型及侧车文件
downloads/  模型下载暂存
logs/       运行和调试日志
```

`config/minigame-dynamics.json` 保存按住和释放加速度；正常停止时写入，下次启动时读取。本轮坐标和速度历史不会持久化。

## 6. 日志与界面

日志固定为：

```text
logs/run/current.log
logs/run/history.log
logs/debug/current.log
logs/debug/history.log
```

运行模式只记录状态和错误；调试模式增加推理、检测框、控制决策，以及带独立操作编号的收杆/抛竿时间线。每种模式只保留当前和上一会话，总目录上限为 `2.5 MiB`。

页面固定为运行、模型、使用指南和设置。启停只由局内热键控制。运行和调试模式共用右上角状态卡：主行显示抛竿、等鱼上钩、和鱼搏斗中、收竿等当前阶段，次行显示停止热键；模式图标与状态卡组成同一视觉单元。调试模式另外显示最新识别框和置信度，过期内容自动隐藏。操作编号只进入调试日志，不再占用游戏画面。
