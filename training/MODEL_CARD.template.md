# VRC-Fisher Model Card

> Replace every `TBD` before publishing a model version. The release script
> rejects a card that still contains `TBD`.

> **Validation leakage warning:** state whether adjacent frames from the same
> recording can cross the train/validation split. If they can, explicitly say
> that this is temporal data leakage, that reported validation metrics are
> biased high, and that they do not represent performance on unseen recordings.

## Identity

| Field | Value |
|---|---|
| Model release | TBD |
| Runtime API | TBD |
| Locator checkpoint/run | TBD |
| Minigame checkpoint/run | TBD |
| Base model | `yolo11n.pt` |
| Ultralytics version | TBD |
| License | AGPL-3.0 (as designated by Ultralytics upstream) |

## Classes

- Locator: `bite_indicator`, `minigame_panel`
- Minigame: `catch_zone`, `moving_target`

## Data

Recordings, frames, annotations, and datasets are private and are not included
in the model release. Record only non-identifying aggregate information here.

| Field | Value |
|---|---|
| Recording source and collection method | TBD |
| Number of independent recordings | TBD |
| Train/validation assignment | TBD; disclose image-level splitting and temporal leakage when applicable |
| Locator image/box counts | TBD |
| Minigame image/box counts | TBD |
| Review status | TBD |

## Training and evaluation

| Field | Value |
|---|---|
| Image sizes | Locator `960 x 960`; minigame `640 x 640` |
| Epochs and selected checkpoints | TBD |
| Device and software environment | TBD |
| Locator metrics | TBD |
| Minigame metrics | TBD |
| Data-leakage effect and independent-recording runtime validation | TBD |

## Files

| File | Bytes | SHA-256 |
|---|---:|---|
| `checkpoints/locator.pt` | TBD | TBD |
| `checkpoints/minigame.pt` | TBD | TBD |
| `runtime/locator.onnx` | TBD | TBD |
| `runtime/minigame.onnx` | TBD | TBD |

## Intended use and limitations

TBD

This model detects UI elements in supported VRChat fishing scenes. It is not
guaranteed to generalize to other worlds, resolutions, themes, occlusion, or
future UI revisions. Use of the model does not grant permission to violate
VRChat terms, world rules, privacy rights, or third-party copyrights.

## License and source

The official PT checkpoints and ONNX runtime models were produced through the
Ultralytics YOLO11 training/export pipeline and are released under the
upstream-designated AGPL-3.0. See `MODEL_LICENSE.txt` beside this card. The
complete model files are stored in `models/vX.Y.Z/` in the repository tag
matching this release; GitHub Release assets are only a runtime download subset.
