# Datasets

`locator/` 和 `minigame/` 存放由 `data_processing/` 按录屏划分后的 YOLO 数据集：

```text
<任务>/
  data.yaml
  images/{train,val,test}/
  labels/{train,val,test}/
```

`data.yaml` 和空目录结构可以提交；图片与标签由 Git 忽略。不要手工把同一录屏的相邻帧随机拆到不同集合。
