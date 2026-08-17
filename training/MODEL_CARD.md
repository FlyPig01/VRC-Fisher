# VRC-Fisher Model Card

## Identity

| Field | Value |
|---|---|
| Model release | `0.1.1` |
| Runtime API | `1` |
| Locator checkpoint/run | `training/runs/locator-round5/weights/best.pt` |
| Minigame checkpoint/run | `training/runs/minigame-round3/weights/best.pt` |
| Base architecture | YOLO11n |
| Ultralytics version | `8.4.118` |
| License | AGPL-3.0 (upstream-designated for the Ultralytics-derived training chain and model artifacts) |

This release combines the best checkpoint selected independently for each task:
Locator Round 5 and Minigame Round 3. The runtime manifest permits automatic
input. Labeled validation is complete; independent video and in-application
review remain necessary before final quality acceptance. No claim of universal
scene accuracy is made.

## Classes

- Locator: `bite_indicator`, `minigame_panel`
- Minigame: `catch_zone`, `moving_target`

The mouse controls `catch_zone`; the objective is to keep `moving_target`
inside it. Success, failure, rail, and progress-bar graphics are not model
classes.

## Data

Recordings, extracted frames, annotations, and datasets remain private and are
not included in this release. Complete recordings are assigned to only one
split; adjacent frames from the same recording are never divided between
training and validation.

Because the two tasks use checkpoints from different rounds, their training
sets are documented separately.

| Field | Locator Round 5 | Minigame Round 3 |
|---|---:|---:|
| Recording split | 5 train / 2 validation | 3 train / 1 validation |
| Training images | 1,729 | 598 |
| Validation images | 269 | 81 |
| Training boxes | 884 | 996 |
| Validation boxes | 147 | 134 |
| Reviewed training negatives | 846 | 100 |

Locator Round 5 contains 186 training `bite_indicator` boxes and 698 training
`minigame_panel` boxes. Its validation set contains 80 and 67 boxes
respectively. Minigame Round 3 contains 498 training boxes for each class and
67 validation boxes for each class.

## Training and evaluation

| Field | Locator Round 5 | Minigame Round 3 |
|---|---|---|
| Image size | `960 x 960` | `640 x 640` |
| Batch | 4 | 8 |
| Epochs | 64/100, best epoch 44 | 25/100, best epoch 5 |
| Early stopping | patience 20 | patience 20 |
| Best-epoch Precision | `0.97974` | `0.98248` |
| Best-epoch Recall | `0.99375` | `0.98507` |
| Best-epoch mAP50 | `0.98938` | `0.98072` |
| Best-epoch mAP50-95 | `0.82004` | `0.71639` |

Training used Windows x64, NVIDIA GeForce RTX 4060 Laptop GPU (`cuda:0`),
PyTorch `2.13.0+cu130`, Python 3.11, `workers=4`, `seed=42`,
`deterministic=true`, `amp=true`, and optimizer auto-selection. Both tasks
stopped normally without NaN metrics or corrupt samples.

All selected checkpoints were re-evaluated on their current labeled validation
sets with `conf=0.001` and `iou=0.7`. Rounded unified results were:

| Task | Precision | Recall | mAP50 | mAP50-95 |
|---|---:|---:|---:|---:|
| Locator Round 5 | 0.980 | 0.994 | 0.989 | 0.821 |
| Minigame Round 3 | 0.982 | 0.985 | 0.981 | 0.718 |

The validation confidence is used to construct complete PR curves. It is not
the application runtime threshold. Independent unlabelled videos are reviewed
visually for continuity and box stability and cannot provide mAP, Precision,
or Recall without ground-truth labels.

## Files

| File | Bytes | SHA-256 |
|---|---:|---|
| `checkpoints/locator.pt` | 5497114 | `2db97ba46e3d967026fe0a239cc5b6b46060b79fd87c51ca8e5635faee656b18` |
| `checkpoints/minigame.pt` | 5424602 | `2b4b03aaacfaa34b085ea1ced219a17c78b97903c0af748c07d61d04078af9ab` |
| `runtime/locator.onnx` | 10815002 | `71a026d22508e9bc22557f467ee07009624ece9ea9e6021456da93c117a078c7` |
| `runtime/minigame.onnx` | 10604959 | `3e38ae031b7ad2948e30162eb93de288441671145643a229648db33852a57a4b` |

The ONNX files are FP32, static batch 1, opset 17 graphs without built-in NMS.
Locator input is `[1,3,960,960]`; Minigame input is `[1,3,640,640]`. The same
files can run through ONNX Runtime CPU or DirectML without retraining.

## Intended use and limitations

These models detect the four UI classes in the supported VRChat fishing scenes
represented by the private recordings. They are intended for the project's
local runtime and review tools. They are not guaranteed to generalize to other
worlds, resolutions, themes, UI revisions, scaling animations, occlusion, or
unseen target art. The remaining known model-quality issue is unstable
recognition after large viewpoint changes.

The model release does not grant permission to violate VRChat terms, world
rules, privacy rights, or third-party copyrights. Users must perform their own
live safety and compatibility review before enabling input automation.

## License and source

The PT checkpoints and ONNX runtime models were produced through the
Ultralytics YOLO11 training/export pipeline and are distributed under the
upstream-designated AGPL-3.0. See `MODEL_LICENSE.txt` beside this card and the
complete corresponding source under `models/v0.1.1/`. Original application
and data-processing code remains under the repository's MIT License; these are
separate licensing boundaries.
