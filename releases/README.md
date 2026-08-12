# Release Artifacts

本地构建产物按 GitHub Release 标签分目录生成：

```text
app-vX.Y.Z/       单个 C#/WinUI 3 Windows Setup 和 SHA-256
models-vX.Y.Z/    两个 ONNX 和 model-manifest.json
```

该目录不是开发、训练或正式下载源。所有生成内容均被 Git 忽略；正式资源需要经过验收后发布到 GitHub Releases。
