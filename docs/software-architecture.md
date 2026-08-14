# 软件架构

## 1. 边界

正式软件在 Windows 上捕获用户选择的完整显示器，识别以下流程并控制鼠标：

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
  tests/
    VrcFisher.Core.Tests/
    VrcFisher.Application.Tests/
    VrcFisher.Infrastructure.Tests/
```

| 项目 | 职责 |
|---|---|
| `Core` | 状态机、检测结果、控制决策、时间与输入接口；不依赖 WinUI、ONNX 或 Win32 |
| `Application` | 运行用例、生命周期、配置验证、状态快照和安全停机 |
| `Infrastructure` | 屏幕捕获、ONNX、模型下载、文件存储、窗口检测与鼠标输入 |
| `Desktop` | WinUI 3 页面、ViewModel、导航、本地化和依赖注入入口 |

依赖方向固定为：

```text
Desktop ---------> Application ---------> Core
                         ^
                         |
Infrastructure ----------+
```

ViewModel 不直接调用 ONNX Runtime、GitHub API 或 Win32。后台服务只向 UI 发布不可变、低频的运行快照。

## 3. 实时管线

```text
Windows Graphics Capture（完整显示器）
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

`locator.onnx` 在完整屏幕上解决 UI 移动和缩放，只识别 `bite_indicator` 与 `minigame_panel`。`minigame.onnx` 在局部原始细节上只识别 `catch_zone` 与 `moving_target`。鱼、齿轮或其他需要追踪的物件都归为 `moving_target`；不再识别成功、失败、轨道或进度条。

调度基线：

- 等待 `bite_indicator` 或等待 `minigame_panel` 出现时，locator 目标频率为 10-15 Hz；
- 首次连续确认 `minigame_panel` 后锁定本轮裁剪框，不让 locator 框的逐帧抖动改变 minigame 输入；
- 小游戏期间 minigame 对固定裁剪区域运行 20-30 Hz，locator 降为 2-5 Hz，只确认面板是否消失或位置是否明显异常；
- `catch_zone` 或 `moving_target` 持续丢失时立即释放鼠标，提高 locator 频率并重新定位；
- 每轮小游戏结束后丢弃锁定框，下一轮重新定位；
- 缓冲只保留最新帧，推理落后时丢弃旧帧，不能排队累积延迟；
- 帧缓冲与输入张量预分配并复用；
- 捕获、推理、状态机和输入不得运行在 UI 线程；
- UI 刷新 5-10 Hz，诊断预览默认关闭且最多 5 FPS。

上述频率都是目标范围，不是已经测得的结果。`960` 相比 `640` 的单次像素计算规模约为 2.25 倍，但 `960@10Hz` 的 locator 调用规模约为 `640@30Hz` 的 75%。当前 C# 管线已能分别从两个 ONNX 读取静态输入尺寸，但仍逐帧运行 locator、每帧重新裁剪且使用旧八类契约；变频调度、锁定裁剪、四类契约和输入张量复用必须在正式模型接入前完成。

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
- 调整滑块不能影响正在进行的一轮，下一轮才读取新值；
- 滑块范围和默认值必须根据多段实际录屏的咬钩耗时确定，目前不能凭空写死。

这一计时器只解决感叹号可能出现在捕获画面外的问题。它不是第二套识别方法，也不能保证在咬钩时间高度随机的世界中可靠工作。

安全要求：

- 默认进入“仅观察”，不得自动发送输入；
- 自动运行前验证模型、捕获目标、实际 Provider 和模型清单的 `automatic_allowed=true`；发送每次自动输入前还要确认 VRChat 是前台进程；
- 关键目标缺失、帧过旧、捕获中断或推理异常时立即释放鼠标；
- `F8` 全局紧急停止独立于 UI 线程，在任何状态都能释放鼠标；
- 停止和退出操作必须幂等，异常也不能留下按住状态。

## 5. WinUI 3 前端

主窗口是紧凑的 Windows 工具界面，不持续播放完整屏幕：

| 页面 | 内容 |
|---|---|
| 运行 | VRChat 状态、仅观察、自动运行、停止、当前阶段和实际 Provider |
| 模型 | 下载、更新、删除、版本、文件大小和完整性 |
| 设置 | 当前已实现捕获目标和设备选择，并显示软件根目录；屏外感叹号兜底时间滑块、阈值与其他超时编辑尚未接入 |
| 诊断 | 当前已实现 Provider、捕获帧数、丢弃帧数和状态；推理延迟、帧龄、CPU/内存、日志入口和预览尚未接入 |

界面使用 WinUI 3 原生 Fluent 控件、明暗主题、系统强调色与可选 Mica。布局不使用营销式大标题、堆叠卡片、大面积渐变或持续动画。“仅观察”和“自动运行”必须在文字、图标和状态色上明确区分。

界面语言资源由随应用编译的 `.resw` 提供简体中文和 English。它们随 Setup 安装，不是 GitHub Release 资产；安装器的语言选择写入安装目录的配置文件，应用启动时选择对应资源，不需要单独下载语言包。主窗口与页面主要固定文本已接入资源键；部分底层动态错误仍可能直接显示原始错误文本，首版也不提供运行中即时切换语言。

## 6. 配置与本地文件

应用以 `<安装目录>\\program\\VrcFisher.exe` 的父目录作为软件根目录。模型、配置、日志、下载暂存与诊断产物只能写入用户选择的安装目录，具体结构见 [安装与发布](installation-and-release.md)。

这一要求意味着安装目录必须对当前用户可写。应用不能把运行数据转移到 `%LOCALAPPDATA%`、`ProgramData`、用户主目录或注册表数据目录。

## 7. 实现顺序和完成条件

1. 将 C# 检测契约、状态机和测试从旧八类同步为四类，并加入屏外感叹号兜底时间。
2. 接入 Windows Graphics Capture、系统显示器/窗口选择器、D3D11 surface readback 与最新帧缓冲（已接入代码，尚未在真实 VRChat 场景验收）。
3. 将两个 ONNX 会话的类别映射同步为 locator 两类与 minigame 两类，完成预处理、后处理和录屏回放。
4. 完成观察模式，验证状态转换和故障释放。
5. 接入真实鼠标与全局 `F8`。
6. 完成 WinUI 页面、模型管理和诊断（模型下载、取消、有限重试和删除已接入；完整诊断指标、真实 Release 和现场验收仍未完成）。
7. 在 CPU-only 与 DirectML 下完成性能和现场验收。

没有正式数据和两个有效 ONNX 时，可以建立接口、状态机和张量/回放测试，但不得声称识别链路已完成。当前真实 WGC 适配已可构建，但 C# 仍使用旧八类检测契约，兜底时间滑块也未实现；这些必须在按新四类基线训练前同步。由于仓库没有有效模型和人工标注数据，仍不能报告现场识别准确率或自动钓鱼成功率。
