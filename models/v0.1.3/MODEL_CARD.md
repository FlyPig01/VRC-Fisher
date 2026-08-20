# VRC-Fisher Model Card

## Identity

| Field | Value |
|---|---|
| Model release | `0.1.3` |
| Runtime API | `1` |
| Locator checkpoint/run | `training/runs/locator-round7/weights/best.pt` |
| Minigame checkpoint/run | `training/runs/minigame-round6/weights/best.pt` |
| Base architecture | YOLO11n |
| Ultralytics version | `8.4.118` |
| License | AGPL-3.0 (upstream-designated for the Ultralytics-derived training chain and model artifacts) |

> **Validation leakage warning:** this release uses an image-level random split.
> Adjacent frames from the same source recording can appear in both training and
> validation. The reported validation metrics are therefore biased high and do
> not measure generalization to unseen recordings, viewpoints, systems, or UI
> variants. They must not be presented as real-world accuracy.

This release contains Locator Round 7 and Minigame Round 6. The Locator was
retrained with the reviewed `反馈10` samples; the Minigame checkpoint and
runtime model are carried forward unchanged from Round 6. Runtime manifest API
1 permits automatic input. Independent full-video and in-application review
remain the practical quality checks; no claim of universal scene accuracy is
made.

## Classes and acceptance priority

- Locator: `bite_indicator`, `minigame_panel`
- Minigame: `catch_zone`, `moving_target`

`bite_indicator` prioritizes Precision because a false positive can advance the
fishing flow incorrectly, while the animated indicator persists long enough for
later detections to recover from occasional misses. `minigame_panel`,
`catch_zone`, and `moving_target` prioritize Recall. The two minigame classes
also require stable, close-fitting boxes because the controller keeps
`moving_target` inside `catch_zone`.

Success, failure, rail, progress-bar, and decorative graphics are not model
classes.

## Data

Recordings, extracted frames, annotations, and datasets remain private and are
not included in this release. Both tasks use a seeded image-level stratified
random split: 90% training and 10% validation with seed 42. Positive and
negative images are stratified separately. One exact image cannot occur in both
sets, but adjacent and near-duplicate frames from one recording can cross the
split. This is temporal data leakage and inflates all validation metrics.

| Field | Locator Round 7 | Minigame Round 6 |
|---|---:|---:|
| Training images | 2,774 | 977 |
| Validation images | 308 | 108 |
| Training positive images | 1,295 | 830 |
| Validation positive images | 144 | 92 |
| Training negative images | 1,479 | 147 |
| Validation negative images | 164 | 16 |
| Training boxes | 1,295 | 1,660 |
| Validation boxes | 144 | 184 |

Locator training contains 465 `bite_indicator` and 830 `minigame_panel`
boxes; validation contains 52 and 92. Minigame training contains 830 boxes per
class; validation contains 92 boxes per class.

## Training

| Field | Locator Round 7 | Minigame Round 6 |
|---|---:|---:|
| Image size | `960 x 960` | `640 x 640` |
| Batch | 4 | 8 |
| Epochs | 100/100, best epoch 89 | 99/100, best epoch 79 |
| Early stopping | patience 20 | patience 20 |
| Best-epoch Precision | `0.99599` | `0.99916` |
| Best-epoch Recall | `0.99038` | `1.00000` |
| Best-epoch mAP50 | `0.99382` | `0.99500` |
| Best-epoch mAP50-95 | `0.87513` | `0.83662` |

Training used Windows x64, NVIDIA GeForce RTX 4060 Laptop GPU (`cuda:0`),
PyTorch `2.13.0+cu130`, Python 3.11.4, `workers=4`, `seed=42`,
`deterministic=true`, `amp=true`, and optimizer auto-selection (AdamW). The
Locator run completed 100 epochs without NaN metrics, corrupt samples, GPU
errors, or out-of-memory errors.

## Evaluation

The selected Locator checkpoint was evaluated at its best epoch on the same
leaked image-level validation split. The Minigame values are carried forward
from Round 6. Evaluation used `conf=0.001` and `iou=0.7`; the low validation
confidence constructs complete PR curves and is not the application runtime
threshold.

| Task/class | Precision | Recall | mAP50 | mAP50-95 |
|---|---:|---:|---:|---:|
| Locator overall | 0.99599 | 0.99038 | 0.99382 | 0.87513 |
| Minigame overall | 0.99916 | 1.00000 | 0.99500 | 0.83662 |
| `catch_zone` | 0.99900 | 1.00000 | 0.99500 | 0.83900 |
| `moving_target` | 0.99900 | 1.00000 | 0.99500 | 0.83100 |

These values are useful only for comparing checkpoints on this exact split.
Because of temporal leakage, they are expected to be higher than performance
on new user recordings. Independent unlabelled videos can be reviewed for
false positives, continuity, relocalization, and box stability, but cannot
provide Precision, Recall, or mAP without ground-truth labels.

## Files

| File | Bytes | SHA-256 |
|---|---:|---|
| `checkpoints/locator.pt` | 5501722 | `a47178f9e87398c6bb35b9e86aaf2db7ad06fa6be46dd11bafb55796eb8b2ad0` |
| `checkpoints/minigame.pt` | 5436698 | `444ecec97bf833a49e438c4da2ccc62cbcd8fa8ac60259964c053af4bc29412a` |
| `runtime/locator.onnx` | 10815002 | `e67556297671c126855f3be7ca0d745d3720c7859d66e4762624ee0514e1abd6` |
| `runtime/minigame.onnx` | 10604959 | `075f0b9dbe4f73a62d4c4a0c183792748669b55f36a65e6ef348175fa63e9a25` |

The ONNX files are FP32, static batch 1, opset 17 graphs without built-in NMS.
Locator input/output is `[1,3,960,960]` / `[1,6,18900]`; Minigame is
`[1,3,640,640]` / `[1,6,8400]`. The same files run through ONNX Runtime CPU or
DirectML without retraining.

## Intended use and limitations

These models detect four UI classes in the supported VRChat fishing scene
represented by the private recordings. They are intended for this project's
local runtime and review tools. They are not guaranteed to generalize to other
worlds, resolutions, themes, UI revisions, scaling animations, occlusion,
viewpoint changes, or unseen target art. False-positive and relocalization
behavior must be checked on new full videos before normal release.

The model release does not grant permission to violate VRChat terms, world
rules, privacy rights, or third-party copyrights. Users must perform their own
live safety and compatibility review before enabling input automation.

## License and source

The PT checkpoints and ONNX runtime models were produced through the
Ultralytics YOLO11 training/export pipeline and are distributed under the
upstream-designated AGPL-3.0. See `MODEL_LICENSE.txt` beside this card and the
complete corresponding source under `models/v0.1.3/`. Original application and
data-processing code remains under the repository's MIT License; these are
separate licensing boundaries.
