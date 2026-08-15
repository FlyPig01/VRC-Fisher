# 安装与发布

## 1. 发布物

应用和模型独立发版：

```text
app-vX.Y.Z
  VRC-Fisher-Setup-x64.exe

models-vX.Y.Z
  locator.onnx
  minigame.onnx
  MODEL_CARD.md
  MODEL_LICENSE.txt
  model-manifest.json
```

`app-v*` 不附带独立 `.sha256`；GitHub 页面会显示资产摘要。`models-v*` 必须保留清单中的大小和 SHA-256，软件用它完成下载校验和原子替换。软件版本变化不要求重复发布模型；模型变化也不要求重打软件。

## 2. 用户运行环境

Setup 内含 .NET、WinUI 3、ONNX Runtime DirectML 和全部语言资源。用户不需要预装 Python、.NET、CUDA、Miniconda 或全局 ONNX Runtime。

只发布一套 DirectML 程序，不再提供独立 CPU-only 安装组件：

| 软件选项 | 实际行为 |
|---|---|
| `Auto` | 优先 DirectML，失败时回退 CPU并显示原因 |
| `GPU` | 强制 DirectML，失败即报错 |
| `CPU` | 使用 DirectML 包内的 CPU 后端 |

三种选项使用同一组 FP32 ONNX，不重新训练、转换或下载模型。

## 3. 构建

开发机要求：

- .NET 10 SDK；
- 可恢复仓库锁定的 NuGet 包；
- Inno Setup 6；
- 仅构建应用时不需要 Python 或 CUDA。

在仓库根目录执行：

```powershell
dotnet test app\VrcFisher.sln -c Release -p:Platform=x64

.\packaging\build.ps1 `
  -Version 0.1.2 `
  -Repository FlyPig01/VRC-Fisher
```

输出：

```text
releases/app-v0.1.2/VRC-Fisher-Setup-x64.exe
```

构建脚本会：

1. 清理 `build/installer/stage` 和同版本应用发行目录；
2. `win-x64` 自包含发布唯一 DirectML 程序；
3. 拒绝把 `.onnx` 打入 Setup；
4. 校验 `VrcFisher.pri`、DirectML 依赖和第三方许可证；
5. 写入 `release.json`；
6. 只生成一个 Setup，不生成重复哈希附件。

Setup 是离线完整安装包，不是几 MB 的联网引导器。模型是唯一可选联网下载内容。

`app-v0.1.2` 实测 Setup 为 `57.46 MiB`；最近一次非 C 盘无模型安装为 `215.75 MiB`（含卸载器）。

## 4. 安装向导

一个 Setup 依次完成：

```text
按 Windows UI 语言预选安装器语言
→ 用户可手动切换，后续安装页立即使用该语言
→ 选择安装目录
→ 选择桌面快捷方式
→ 选择是否安装后下载模型
→ 安装
```

20 种安装器语言与软件语言一一映射。没有匹配的系统语言时默认 English。安装器最终语言写入：

```text
<安装目录>\config\installer-language.ini
```

软件首次启动读取该值；之后用户可在软件设置中切换语言。语言资源位于程序的 `VrcFisher.pri`，不从 Release 单独下载。

## 5. 安装目录

默认目录是当前用户可写的文档目录，但用户可以选择任意本地磁盘上的可写目录。Setup 下载位置不等于安装目录。

```text
<安装目录>\
  release.json
  USER_GUIDE.md
  LICENSE
  THIRD_PARTY_NOTICES.md
  licenses\
  program\
  config\
  models\
  downloads\
  logs\
```

程序、设置、模型、下载暂存和日志都位于用户选择的安装目录。允许 Windows 将以下系统级信息保存在系统目录：卸载注册项、开始菜单或桌面快捷方式、安装临时文件、Prefetch、安全与事件记录。这些不用于保存 VRC-Fisher 业务配置。

安装器拒绝不可写目录，也拒绝含无关文件的非 VRC-Fisher 目录。覆盖安装通过 `release.json` 和 `program\VrcFisher.exe` 识别现有安装。

## 6. 模型下载

软件查询仓库中全部非草稿、非预发布的 `models-v*` Release，按版本号选出最新且 `runtime_api` 兼容的清单，不依赖 GitHub 的 `Latest` 标签。

下载流程：

```text
检查清单
→ 下载两个 ONNX、模型卡和模型许可证到 downloads 临时目录
→ 逐文件校验大小和 SHA-256
→ 写入 installed-models.json
→ 原子替换 models 目录
→ 清理临时目录
```

中断、网络错误或校验失败不会破坏已安装模型。界面按总大小固定使用 `B / KB / MB / GB`，例如 `4.1 / 20.5 MB`，下载过程中不切换单位。

模型缺失或损坏时显示蓝色“下载”，存在新版时显示绿色“更新”，已是最新版时显示“删除”。下载、更新、删除都需要二次确认。删除只移除软件管理的两个模型、模型卡、许可证和安装记录。

## 7. 应用升级

应用没有后台自动更新器。用户从新的 `app-v*` 下载 Setup 并覆盖安装到原目录：

- 替换 `program/`、根目录文档和许可证；
- 保留 `config/`、`models/`、`downloads/` 和 `logs/`；
- 设备模式继续在软件内切换；
- 模型由模型页独立检查与更新。

模型单独更新时只创建新的 `models-v*` Release；不需要复制未变化的软件安装包。

## 8. 卸载

使用 Windows“已安装的应用”运行 `unins000.exe`。`unins000.dat` 是卸载数据，不是用户应直接运行的程序。

完整卸载删除安装目录内的软件、模型、设置、下载暂存、日志、许可证和由安装器创建的快捷方式与卸载注册信息。清理只针对已验证安装根目录及明确子目录，不尝试删除 Prefetch、系统事件、Defender 或驱动缓存。

## 9. 发布验收

每次应用发布必须完成：

1. `dotnet test` 全部通过；
2. 20 份 `.resw` 可解析且资源键集合一致；
3. Setup 只有一个 DirectML 程序源，不包含 ONNX；
4. 在新的非 C 盘目录安装并启动；
5. 目录页显示的占用与实际无模型安装目录误差不超过 `5 MiB`；
6. `Auto / GPU / CPU` 均能给出用户选择和实际后端；
7. 模型下载、更新、取消、删除和失败回滚通过；
8. 覆盖安装保留模型与设置；
9. 卸载后不残留软件主动管理的数据；
10. `releases/app-vX.Y.Z/` 只包含 Setup；
11. Git 提交使用中文，应用 tag 为 `app-vX.Y.Z`；模型未变化时不创建新模型 tag。
