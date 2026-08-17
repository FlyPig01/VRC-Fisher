# VRC-Fisher

VRC-Fisher 是 Windows 上的 VRChat 钓鱼自动化项目。正式软件使用 C#、WinUI 3、Windows Graphics Capture、ONNX Runtime DirectML 和 SendInput，只处理前台 `VRChat.exe` 窗口。

当前应用版本已完成自动收杆、下一轮流程和低识别帧率控制的现场验证；现存未关闭问题仅为两个模型的识别质量仍需改进。

## 快速使用

1. 从 `app-v*` Release 安装软件。
2. 在模型页下载两个模型。
3. 进入 [Fins Fishing](https://vrchat.com/home/world/wrld_ae001ea3-ed05-42f0-adf2-3d47efd10a77/info)，拿起钓竿并让感叹号和小游戏 UI 可见。
4. 保持 VRChat 在前台，按启动热键；默认 `F8`。

完整安装、设置和故障处理见 [使用手册](USER_GUIDE.md)。

## 开发入口

| 工作 | 文档 |
|---|---|
| 开发环境和测试 | [开发与环境](docs/development.md) |
| C# 软件 | [app/README.md](app/README.md) |
| 录屏与标注 | [data_processing/README.md](data_processing/README.md) |
| 训练与导出 | [training/README.md](training/README.md) |
| 安装包与模型包 | [packaging/README.md](packaging/README.md) |
| 当前架构和缺陷 | [docs/README.md](docs/README.md) |

## 仓库目录

```text
app/              C# 正式软件与测试
data_processing/  录屏、标注和数据集生成
training/         模型训练、评估和导出
models/           已验收并随源码公开的模型
packaging/        发布构建脚本
docs/             当前设计与历史归档
releases/         本地发布输出，不提交 Git
```

Python 只用于离线数据处理和训练，不进入用户软件。Setup 是 self-contained DirectML 程序，用户不需要 Python、CUDA、Miniconda 或预装 .NET。

## 许可证

原创应用和通用工具代码采用 MIT；`training/`、Ultralytics 训练链及其衍生模型按上游 AGPL-3.0；第三方组件遵循各自许可证。详情见 [发布与许可](docs/release.md)。
