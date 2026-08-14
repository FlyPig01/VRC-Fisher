# Release Artifacts

本地构建产物按 GitHub Release 标签分目录生成：

```text
app-vX.Y.Z/       单个 C#/WinUI 3 Windows Setup 和 SHA-256；Setup 内含许可证与第三方声明
models-vX.Y.Z/    两个 ONNX、model-manifest.json、MODEL_CARD.md 和 MODEL_LICENSE.txt
```

该目录不是开发、训练、源码存储或正式下载源。所有生成内容均被 Git 忽略；正式模型的两个 `.pt` 和两个 ONNX 已随代码保存在 `models/vX.Y.Z/`，这里的模型 Release 只能由该目录生成并作为最终用户的下载副本。官方模型按上游标注的 AGPL-3.0 发布，不能省略模型卡或模型许可证；schema v2 清单必须记录两份 ONNX 和两份侧车文件的大小与 SHA-256。
