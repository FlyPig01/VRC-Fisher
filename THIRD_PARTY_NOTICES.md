# Third-Party Notices

VRC-Fisher contains original code and uses third-party software. The root
`LICENSE` applies only to original VRC-Fisher material unless a more specific
license is present. Third-party software and model artifacts remain under their
own licenses.

This inventory records the direct dependencies pinned by the repository as of
2026-08-15. A release maintainer must review the resolved lock files and final
binary output again whenever a dependency is changed.

## Distributed application dependencies

| Component | Version | License | Source |
|---|---:|---|---|
| Microsoft Windows App SDK NuGet distribution | 1.8.260710003 | Microsoft Software License Terms plus bundled notices; upstream source repository is MIT | https://github.com/microsoft/WindowsAppSDK |
| Microsoft.Extensions.Logging | 10.0.0 | MIT | https://github.com/dotnet/runtime |
| Microsoft.ML.OnnxRuntime.DirectML | 1.24.4 | MIT | https://github.com/microsoft/onnxruntime |
| .NET self-contained runtime | 10.x | MIT and bundled third-party notices | https://github.com/dotnet/runtime |
| Inno Setup | 6.x | Inno Setup License; commercial users are requested to purchase a license, while the official FAQ says purchase is not strictly required | https://jrsoftware.org/isorder.php |
| Inno Setup Chinese Simplified translation | pinned source in `packaging/languages/` | upstream terms and retained file header | https://github.com/jrsoftware/issrc |

The installer carries this notice, the VRC-Fisher MIT license, and the
AGPL-3.0 text applicable to the optional official model downloads. Licenses
and notices embedded in published third-party binaries remain applicable.

The locally verified Inno Setup 6.7.3 compiler prints `Non-commercial use
only`, while its installed `license.txt` grants use for any purpose including
commercial applications. The official commercial-license FAQ clarifies that a
purchase is requested from qualifying commercial users but is not strictly
required. Release maintainers must preserve the installed license text and
recheck the official terms when changing compiler versions.

## Offline development dependencies

These packages are not included in the C# application installer.

| Area | Component | Resolved version | License |
|---|---|---:|---|
| Data processing | PyAV | 16.1.0 | BSD-3-Clause; its binary media dependencies require a separate final-artifact audit if redistributed |
| Data processing | NumPy | 2.0.2 | BSD-3-Clause and bundled permissive licenses |
| Data processing | Pillow | 12.3.0 | MIT-CMU |
| Testing | pytest | 8.4.2 | MIT |
| Training | PyTorch | 2.13.0 | BSD-style and bundled third-party licenses |
| Training | torchvision | 0.28.0 | BSD-3-Clause |
| Training | Ultralytics | 8.4.118 | AGPL-3.0 as identified by upstream; its PyPI classifier says AGPLv3+ |
| Export | ONNX | 1.22.0 | Apache-2.0 |
| Export | onnxslim | 0.1.95 | MIT |

The full AGPL-3.0 text is stored in `training/LICENSE`. Other dependency
license texts are distributed by their package archives and linked above.
Redistributors remain responsible for preserving every notice required by the
exact artifacts they ship, including transitive native libraries.

## Models

The official VRC-Fisher models are produced through the Ultralytics YOLO11
training and export pipeline. They are not licensed under the root MIT license.
Following Ultralytics' published licensing position, official `.pt` checkpoints
and derived `.onnx` files are released under the upstream-designated AGPL-3.0 and must include
a model card and model license. Every accepted model version stores both `.pt`
checkpoints and both `.onnx` runtime files in `models/vX.Y.Z/` in the source
repository. GitHub model releases are an end-user download subset, not the
exclusive source for the weights.

No code, model weights, screenshots, labels, documentation, or media from
`day123123123/vrc-auto-fish` are included. VRC-Fisher independently implements
general ideas such as screen detection, a fishing state machine, and feedback
control.

## Data

Recordings, extracted frames, annotations, review images, and generated
datasets are excluded from the repository. They are not covered by the root MIT
license and no public data license is granted. This reduces privacy and
third-party content risk; it does not transfer ownership of VRChat, world,
avatar, font, or other third-party material.

## No platform permission

Open-source licenses grant copyright permissions only. They do not grant
permission to violate VRChat terms, a world's rules, trademarks, privacy
rights, or any other third-party rights.
