"""Launch CUDA pre-labelling from the lightweight data-processing environment."""

from __future__ import annotations

import argparse
from pathlib import Path
import subprocess


REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_LOCATOR = REPOSITORY_ROOT / "training/runs/locator-best-init/weights/best.pt"
DEFAULT_MINIGAME = REPOSITORY_ROOT / "training/runs/minigame-best-init/weights/best.pt"


def _absolute(path: Path) -> Path:
    return path if path.is_absolute() else (Path.cwd() / path).resolve()


def _choose_input_video() -> Path:
    try:
        import tkinter as tk
        from tkinter import filedialog
    except ImportError as error:
        raise RuntimeError("no --input was provided and the Windows file picker is unavailable") from error

    root = tk.Tk()
    root.withdraw()
    root.attributes("-topmost", True)
    try:
        selected = filedialog.askopenfilename(
            title="选择需要预标注的录屏",
            filetypes=(("视频文件", "*.mp4 *.mkv *.mov *.avi"), ("所有文件", "*.*")),
        )
    finally:
        root.destroy()
    if not selected:
        raise RuntimeError("no input video was selected")
    return Path(selected)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--input",
        type=Path,
        help="video path; omit on Windows to choose a file interactively",
    )
    parser.add_argument("--locator", type=Path, default=DEFAULT_LOCATOR, help="locator model path")
    parser.add_argument("--minigame", type=Path, default=DEFAULT_MINIGAME, help="minigame model path")
    parser.add_argument(
        "--frames-root",
        type=Path,
        default=Path("work/frames"),
        help="directory for extracted frame batches",
    )
    parser.add_argument(
        "--batches-root",
        type=Path,
        default=Path("work/annotations"),
        help="directory for prelabels and annotation drafts",
    )
    parser.add_argument("--interval", type=float, default=0.5)
    parser.add_argument("--quality", type=int, default=95)
    parser.add_argument("--device", default="0")
    parser.add_argument("--bite-confidence", type=float, default=0.15)
    parser.add_argument("--panel-confidence", type=float, default=0.20)
    parser.add_argument("--zone-confidence", type=float, default=0.20)
    parser.add_argument("--target-confidence", type=float, default=0.05)
    parser.add_argument("--padding", type=float, default=0.0)
    parser.add_argument("--max-frames", type=int)
    parser.add_argument("--replace", action="store_true")
    args = parser.parse_args(argv)

    try:
        input_path = args.input or _choose_input_video()
    except (OSError, RuntimeError) as error:
        parser.error(str(error))

    training_python = REPOSITORY_ROOT / "training/.venv/Scripts/python.exe"
    if not training_python.is_file():
        parser.error("training environment not found; deploy training/.venv before pre-labelling")

    command = [
        str(training_python),
        "-m",
        "vrc_training.annotation_package",
        "--input",
        str(_absolute(input_path)),
        "--locator",
        str(_absolute(args.locator)),
        "--minigame",
        str(_absolute(args.minigame)),
        "--frames-root",
        str(_absolute(args.frames_root)),
        "--batches-root",
        str(_absolute(args.batches_root)),
        "--interval",
        str(args.interval),
        "--quality",
        str(args.quality),
        "--device",
        args.device,
        "--bite-confidence",
        str(args.bite_confidence),
        "--panel-confidence",
        str(args.panel_confidence),
        "--zone-confidence",
        str(args.zone_confidence),
        "--target-confidence",
        str(args.target_confidence),
        "--padding",
        str(args.padding),
    ]
    if args.max_frames is not None:
        command.extend(("--max-frames", str(args.max_frames)))
    if args.replace:
        command.append("--replace")
    return subprocess.run(command, cwd=REPOSITORY_ROOT / "training", check=False).returncode


if __name__ == "__main__":
    raise SystemExit(main())
