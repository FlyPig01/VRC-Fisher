# Datasets

`locator/` 和 `minigame/` 存放由 `data_processing/` 按录屏划分后的 YOLO 数据集：

```text
<任务>/
  data.yaml
  split.json
  images/{train,val}/
  labels/{train,val}/
```

`split.json` 只记录整段录屏的 `train`/`val` 归属，供训练前预检和人工审核使用。图片、标签和本地划分结果由 Git 忽略；不要手工把同一录屏的相邻帧随机拆到不同集合。完整视频测试另放在 `training/test/videos/`，不需要标签。
