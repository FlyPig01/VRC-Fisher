# 软件架构

## 1. 边界

正式软件在 Windows 上按进程名精确查找 `VRChat.exe`，并通过主窗口句柄创建 Windows Graphics Capture 捕获项。软件不提供显示器、任意窗口或其他进程选择，识别以下流程并控制鼠标：

```text
左键抛竿
  -> 等待 bite_indicator；屏外时由用户设置的最短等待时间兜底
  -> 左键收钩
  -> 定位 minigame_panel
  -> 按住/松开左键，控制 catch_zone 上下移动并包住 moving_target
  -> minigame_panel 连续消失
  -> 左键收杆
  -> 下一轮
```

软件不注入 VRChat、不读取游戏内存、不修改网络或游戏文件。视觉模型负责识别画面；OpenCV 模板匹配、固定坐标和颜色阈值都不是识别回退方案。唯一非视觉触发是用户明确配置的屏外感叹号计时兜底。模型缺失、画面中断或小游戏关键目标持续缺失时必须释放输入并停止或复位。

## 2. C# 解决方案

目标目录：

```text
app/
  VrcFisher.sln
  src/
    VrcFisher.Core/
    VrcFisher.Application/
    VrcFisher.Infrastructure/
    VrcFisher.Desktop/
      Capture/
      Contracts/
      Localization/
      Pages/
      Ui/
  tests/
    VrcFisher.Core.Tests/
```

| 项目 | 职责 |
|---|---|
| `Core` | 状态机、检测结果、控制决策、时间与输入接口；不依赖 WinUI、ONNX 或 Win32 |
| `Application` | 运行用例、生命周期、配置验证、状态快照和安全停机 |
| `Infrastructure` | 屏幕捕获、ONNX、模型下载、文件存储、窗口检测与鼠标输入 |
| `Desktop` | WinUI 3 页面、固定导航壳、本地化、Windows 捕获适配和依赖组装入口 |

依赖方向固定为：

```text
Desktop -------------------------------> Infrastructure
   |                                           |
   +---------------> Application <------------+
                          |
                          v
                         Core
```

`Desktop/Pages` 只依赖 `IDesktopPageContext`、Application/Core 契约和共享 UI，不反查窗口，也不直接调用 ONNX Runtime、GitHub API、Windows Graphics Capture 或 Win32。`MainWindow` 只负责固定导航、页面创建和授权上下文；`App` 是唯一依赖组装入口。后台服务只向 UI 发布不可变、低频的运行快照。

Windows 通用边界固定为：Core/Application 只持有捕获、推理、输入、硬件和 UI 通知契约；WGC/COM、DirectML、Win32 和 WinUI 分别留在 Infrastructure/Desktop。运行设备使用结构化字段，不以 Provider 字符串驱动业务；当前会话和当前进程内最近一次成功运行结果分开保存。启停由同一串行门禁和取消令牌协调；捕获层通过结构化失败事件接入首帧门禁，回调先释放帧锁再通知 Application 回滚。平台无关测试验证事务状态，WGC、COM、DirectML 与 WinUI 调度由 Windows 集成验收覆盖。硬件检测失败只显示不可用，不得阻止自动流程。

## 3. 实时管线

```text
Windows Graphics Capture（VRChat 主窗口）
  -> 只保留最新帧的有界缓冲
  -> locator.onnx（960 x 960）
     -> bite_indicator：确认咬钩
     -> minigame_panel：确认小游戏存在，锁定并按框加边距裁剪原始帧
        -> minigame.onnx（640 x 640）
        -> catch_zone + moving_target
        -> 时序确认与状态机
        -> Win32 鼠标输入
  -> RuntimeSnapshot
  -> WinUI 3（5-10 Hz）
```

`locator.onnx` 在完整 VRChat 捕获画面上解决 UI 移动和缩放，只识别 `bite_indicator` 与 `minigame_panel`。`minigame.onnx` 在局部原始细节上只识别 `catch_zone` 与 `moving_target`。鱼、齿轮或其他需要追踪的物件都归为 `moving_target`；不再识别成功、失败、轨道或进度条。

调度基线：

- 等待 `bite_indicator` 或等待 `minigame_panel` 出现时，locator 目标频率为 10-15 Hz；
- 首次连续确认 `minigame_panel` 后锁定本轮裁剪框，不让 locator 框的逐帧抖动改变 minigame 输入；
- 小游戏期间 minigame 对固定裁剪区域运行 20-30 Hz，locator 降为 2-5 Hz，只确认面板是否消失或位置是否明显异常；
- `catch_zone` 或 `moving_target` 持续丢失时立即释放鼠标，提高 locator 频率并重新定位；
- 小游戏缓存面板丢失时按 locator 间隔重新定位，不继续用小游戏高频率运行完整捕获画面的 locator；
- 每轮小游戏结束后丢弃锁定框，下一轮重新定位；
- 缓冲只保留最新帧，推理落后时丢弃旧帧，不能排队累积延迟；
- 帧缓冲与输入张量预分配并复用；
- 捕获、推理、状态机和输入不得运行在 UI 线程；
- UI 状态刷新 5 Hz，不显示持续画面预览或内部性能明细。

当前 C# 调度已实现四类受限间隔：等待 locator `80-250 ms`、Hooking 双模型 `80-250 ms`、小游戏缓存裁剪 `33-67 ms`、面板复查 `250-1000 ms`。每轮首次定位后的面板框用于固定 minigame 裁剪，下一轮再重新定位；面板复查间隔由调度器传入检测器，不再硬编码。输入 Tensor、双线性缩放坐标和原始帧裁剪视图都已复用，ONNX 输出直接解析。

调度器用 `DetectionResult.Workload` 区分 locator、双模型和缓存小游戏，在已有推理前后读取 `Stopwatch`，不额外运行模型。每类丢弃 10 次预热、至少积累 30 个有效样本，每 5 秒计算最近 120 个样本的 P95；变慢时立即放宽，稳定 30 秒后才逐步加快。缓存小游戏到 `67 ms` 仍超过 65% 预算时只警告，不继续降频。完整公式和开销见 [性能与存储预算](performance-budget.md)。

## 4. 状态机与输入安全

| 状态 | 进入条件 | 动作 | 超时或异常处理 |
|---|---|---|---|
| `IDLE` | 用户启动或上一轮完成 | 左键一次，抛竿 | 进入 `WAITING_BITE` |
| `WAITING_BITE` | 抛竿完成 | 连续确认 `bite_indicator` 后左键一次；若始终不可见，则达到用户设置的兜底等待时间后只点击一次 | 点击后进入 `HOOKING` |
| `HOOKING` | 已执行收钩点击 | 等待连续确认 `minigame_panel` | UI 未在限定时间内出现时释放输入并复位，不得连续点击 |
| `MINIGAME` | 连续确认 `minigame_panel` | 根据 `moving_target` 与 `catch_zone` 的纵向关系按住或松开左键，控制 `catch_zone` 上下移动 | 关键目标持续缺失时释放左键；UI 连续消失后进入 `REELING` |
| `REELING` | 小游戏 UI 连续消失 | 保证左键已释放，再左键一次收杆 | 点击后进入 `CYCLE_DELAY` |
| `CYCLE_DELAY` | 已执行收杆点击 | 不发送输入，等待轮次间隔 | 间隔结束后回到 `IDLE` 并开始下一轮 |
| `RECOVERY` | 捕获、推理或状态超时 | 释放全部输入 | 延迟后重新开始，或按错误级别停止 |

`bite_indicator` 和 `minigame_panel` 必须由连续的新推理结果确认；缓存结果不能重复计为多帧证据。小游戏结束不依赖 `success` 或 `failure` 类，只以 `minigame_panel` 连续消失为准。

小游戏控制使用边界而不是额外识别轨道：

- `moving_target` 中心高于 `catch_zone` 上边界时，按住左键使捕获区域上升；
- `moving_target` 中心低于 `catch_zone` 下边界时，松开左键使捕获区域下降；
- 目标中心处于捕获区域内时保持当前输入状态，并使用迟滞避免边界附近快速抖动；
- 任一关键框持续丢失时先松开左键，不允许盲目保持按下。

### 屏外感叹号兜底时间

设置页必须提供以秒为单位的滑块，语义固定为：**抛竿后没有检测到 `bite_indicator` 时，允许执行一次收钩点击之前的最短等待时间**。

- 识别到 `bite_indicator` 时立即收钩，不等待滑块计时结束；
- 只有本轮始终没有识别到感叹号时才使用兜底计时；
- 每轮最多触发一次兜底收钩，之后必须等待 `minigame_panel` 或进入恢复；
- 调整滑块后状态机立即读取新值；
- 兜底使用独立开关，默认禁用；滑块范围固定为 `5-30` 秒，默认 `15` 秒；关闭时完全依赖感叹号识别。

这一计时器只解决感叹号可能出现在捕获画面外的问题。它不是第二套识别方法，也不能保证在咬钩时间高度随机的世界中可靠工作。

安全要求：

- 默认保持停止；用户通过只在 VRChat 前台响应的启动/停止热键明确启动，运行页不提供启停按钮；
- 启动事务依次验证模型、推理会话、WGC 捕获和首个有效帧，全部成功后才发布“运行中”并激活状态机；
- 关键目标缺失、帧过旧、捕获中断或推理异常时立即释放鼠标；
- 正常运行通过按键状态轮询实现，不长期调用 `RegisterHotKey`；保存前仅临时注册以检查当时的全局冲突，随后立即注销；
- 热键默认 `F8`，功能键可单独使用，字母和数字必须搭配 `Ctrl / Alt / Shift`；修改流程为两次确认和一次按键捕获；
- 切出、最小化、锁屏、休眠或退出 VRChat 时立即释放鼠标并完整停止，返回后不自动恢复；
- 启停共用串行门禁和取消令牌，重复启动、启动中停止和快速切换必须幂等。

## 5. WinUI 3 前端

主窗口是紧凑的 Windows 工具界面，不持续播放捕获画面：

| 页面 | 内容 |
|---|---|
| 运行 | 当前阶段、VRChat 进程、模型、静态硬件、用户选择与实际运行设备；不放置启停按钮 |
| 模型 | 直接显示两个模型的用途和状态；打开页面时检查新版；按缺失、可更新或已是最新版显示蓝色下载、绿色更新或删除按钮；显示模型目录、总占用和 `owner/repository` 发布源 |
| 使用指南 | 显示随安装包发布的目标世界封面；整图点击官方世界页面；只保留三个开始步骤 |
| 设置 | 界面语言、运行/调试模式、设备选择、局内热键修改、软件目录和默认禁用的 `5-30` 秒屏外感叹号兜底 |

侧栏固定显示且不可收起，不重复放置小品牌图标或小软件名，入口顺序为运行、模型、使用指南、设置。界面使用 WinUI 3 原生 Fluent 控件、明暗主题和系统强调色；不使用营销式大标题、嵌套卡片、大面积渐变或持续动画。

自动钓鱼运行时由 Desktop 层维护独立的 Win32 点击穿透覆盖层，只跟随前台 VRChat 客户区。启动事务期间显示“正在启动”，成功后显示当前停止热键，启动失败后显示 6 秒红色提示；这些提示均不激活软件窗口。调试模式额外复用推理层已经产生的四类最终结果，以固定 `15 Hz` 绘制细边框和 `0.00-1.00` 置信度数字。覆盖层由若干无激活、无任务栏项的窄窗口组成，不遮挡框内画面，也不进入以 VRChat 窗口为目标的 WGC 捕获。

显示防抖位于 Application 层：同类框坐标和置信度使用 `alpha=0.42` 的指数平滑，约对应 `90 ms` 视觉响应；连续两次推理未检出时暂时保留，第三次才隐藏，超过 `500 ms` 的整帧结果直接作废。大于画面宽或高 `15%` 的位置跳变直接吸附，避免 UI 真正移动后产生长距离滞后。该数据流只供覆盖层读取，`FishingStateMachine` 仍直接消费未平滑 `DetectionObservation`。

界面语言资源由随应用编译的 `.resw` 提供 20 种实际语言。它们随 Setup 安装，不是 GitHub Release 资产；首次启动读取 Setup 最终语言，之后设置页只列出以母语名称显示的实际语言，不提供“跟随系统”。选择会写入安装目录的 `config/user.json` 并立即重建当前页面，不需要单独下载语言包。启动被拒绝和运行时致命故障通过全局醒目通知说明原因与处理方法；不可预判的底层故障仍会附带原始错误文本以便排查。

## 6. 配置与本地文件

应用以 `<安装目录>\\program\\VrcFisher.exe` 的父目录作为软件根目录。模型、配置、日志与下载暂存只能写入用户选择的安装目录，具体结构见 [安装与发布](installation-and-release.md)。用户设置写入 `config/user.json`，性能画像写入 `config/performance-profiles.json`。

这一要求意味着安装目录必须对当前用户可写。应用不能把运行数据转移到 `%LOCALAPPDATA%`、`ProgramData`、用户主目录或注册表数据目录。

## 7. 实现顺序和完成条件

1. 四类检测契约、状态机、屏外感叹号兜底、双模型预处理/后处理和动态调度（已实现并有自动化测试）。
2. 仅限 `VRChat.exe` 主窗口的 Windows Graphics Capture、D3D11 surface readback、首帧门禁、真实鼠标、局内热键和 15 Hz 调试覆盖层（已实现；仍需真实 VRChat 现场验收）。
3. round3 最佳 PT 导出静态 FP32 ONNX，并在 CPU 与 DirectML 下验证模型加载、输出解码和真实全屏帧；模型已固化为 `models/v0.1.0/`（已完成）。
4. 人工审核完整 ONNX 带框视频，并在真实 VRChat 中完成状态转换、自动输入和故障释放验收。
5. 在真实 VRChat 下复测自动调频的推理 P95、帧龄、资源占用和 VRChat 帧率影响；输入张量、缩放坐标与裁剪视图复用已经完成。
6. 模型卡、许可文件、来源清单和 `models/v0.1.0/` 已完成；正式 Setup 已构建，GitHub Release 待维护者审核后创建。

当前可以证明 `models/v0.1.0` 的两个 ONNX 已被 C# 的 CPU 和 DirectML 路径正确加载并在抽样帧上产生四类结果。清单已设置 `automatic_allowed=true` 以进行实机自动流程验证，但仍不能据此报告现场识别准确率、资源占用或自动钓鱼成功率。`app/models/` 只是该版本的本地开发副本。
