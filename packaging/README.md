# Packaging

此目录将承载 C# self-contained publish 与单一 Inno Setup 安装器的构建文件。

## 当前状态

现有 `build*.ps1`、`vrc_fisher.spec`、`entrypoint.py` 和 `installer.iss` 仍面向旧 Python/PyInstaller 运行版，不符合正式 C#/WinUI 3 发布设计，不能用于发布。它们不应被误认为已经生成了可用 Setup。

代码阶段需要在 C# 解决方案可发布后重写本目录，目标产物只有一个 `VRC-Fisher-Setup-x64.exe`。安装器、模型 Release、更新与卸载契约见 [安装与发布设计](../docs/installation-and-release.md)。
