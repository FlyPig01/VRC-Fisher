# VRC-Fisher Model Card

## Identity

| Field | Value |
|---|---|
| Model release | `0.1.0` |
| Runtime API | `1` |
| Locator checkpoint/run | `training/runs/locator-round3/weights/best.pt` |
| Minigame checkpoint/run | `training/runs/minigame-round3/weights/best.pt` |
| Base model | `yolo11n.pt` |
| Ultralytics version | `8.4.118` |
| License | AGPL-3.0 (upstream-designated for the Ultralytics-derived training chain and model artifacts) |

This is the current best pair selected by the project maintainer for the
`0.1.0` model release. The release is usable for development and observation;
the runtime manifest deliberately keeps `automatic_allowed=false` until a
separate live input-safety acceptance is completed.

## Classes

- Locator: `bite_indicator`, `minigame_panel`
- Minigame: `catch_zone`, `moving_target`

The mouse controls `catch_zone`; the control objective is to keep
`moving_target` inside it. Success, failure, rail, and progress-bar graphics
are not model classes.

## Data

Recordings, extracted frames, annotations, and datasets remain private and are
not included in this release. The split is performed by complete recording,
not by randomly mixing adjacent video frames.

| Field | Value |
|---|---|
| Recording source and collection method | Private full-screen VRChat recordings; frames were extracted and manually reviewed/annotated |
| Independent recordings | Locator: 6 total (4 train, 2 val); minigame: 4 total (3 train, 1 val) |
| Train/validation assignment | Locator: 1,443 train images / 269 val images; minigame: 598 train images / 81 val images |
| Locator image/box counts | Train 1,443 images / 627 boxes; val 269 images / 147 boxes; 626 train labels are non-empty and the remainder are reviewed negative images |
| Minigame image/box counts | Train 598 images / 996 boxes; val 81 images / 134 boxes |
| Review status | Dataset format and annotations passed the project preflight. The current round3 best weights are approved as the `0.1.0` model release; no claim of universal scene accuracy is made. |

## Training and evaluation

| Field | Value |
|---|---|
| Image sizes | Locator `960 x 960`; minigame `640 x 640` |
| Epochs and selected checkpoints | Locator: 22/100 epochs, best epoch 2, patience 20; minigame: 25/100 epochs, best epoch 5, patience 20; both stopped early |
| Device and software environment | Windows x64, NVIDIA GeForce RTX 4060 Laptop GPU (`cuda:0`), PyTorch `2.13.0+cu130`, CUDA runtime `13.0`, Python 3.11, `workers=4`, `batch=4/8`, `seed=42`, `deterministic=true`, `amp=true`, `optimizer=auto` |
| Locator metrics at best validation epoch | Precision `0.99844`; Recall `0.90933`; mAP50 `0.95864`; mAP50-95 `0.79819` |
| Minigame metrics at best validation epoch | Precision `0.98248`; Recall `0.98507`; mAP50 `0.98072`; mAP50-95 `0.71639` |
| Independent-recording runtime validation | The unlabelled full-screen review video is for visual inspection of boxes and continuity only. It has no ground-truth labels, so mAP/precision/recall and automatic-fishing success rate are not reported. |

Validation metrics use Ultralytics validation settings (`conf=0.001`,
`iou=0.7`) to construct the PR curves. The application runtime threshold is
separate: `confidence=0.35`, with class-wise NMS performed by the C# runtime.

## Files

| File | Bytes | SHA-256 |
|---|---:|---|
| `checkpoints/locator.pt` | 5491674 | `048863e47334a5cdb82b2e6190271d73857a11521abda420a19c114ef96da007` |
| `checkpoints/minigame.pt` | 5424602 | `2b4b03aaacfaa34b085ea1ced219a17c78b97903c0af748c07d61d04078af9ab` |
| `runtime/locator.onnx` | 10815002 | `9f67a20bfa00e97d565fb6d3a60acd01a6618f484922873d191ebfb691fa4572` |
| `runtime/minigame.onnx` | 10604959 | `ed4887ae85c3999b195235673b0ac9d99dd029dff16a7eff08d44a8918872565` |

The ONNX files are static-batch-1, FP32, opset 17 graphs without built-in
NMS. Locator input is `[1,3,960,960]` and minigame input is
`[1,3,640,640]`. The same files can be executed by ONNX Runtime CPU or
DirectML; changing provider does not require retraining.

## Intended use and limitations

These models detect the four UI classes in the supported VRChat fishing scenes
represented by the private recordings. They are intended for local software
development, observation-mode review, and the project’s documented runtime
pipeline. They are not guaranteed to generalize to other worlds, resolutions,
themes, UI revisions, scaling animations, occlusion, or unseen target art.
The model release does not grant permission to violate VRChat terms, world
rules, privacy rights, or third-party copyrights. Users must perform their own
live safety and compatibility review before enabling input automation.

## License and source

The PT checkpoints and ONNX runtime models were produced through the
Ultralytics YOLO11 training/export pipeline and are distributed under the
upstream-designated AGPL-3.0. See `MODEL_LICENSE.txt` beside this card and the
complete corresponding source under `models/v0.1.0/`. Original application and
data-processing code remains under the repository’s MIT License; these are
separate licensing boundaries.
