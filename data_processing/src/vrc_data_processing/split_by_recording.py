"""Split a generated dataset by recording, never by adjacent frame."""

from __future__ import annotations

import argparse
import json
import random
from pathlib import Path
import shutil

from .generated_output import staged_output


def split_by_recording(
    source: Path,
    output: Path,
    train_ratio: float = 0.8,
    val_ratio: float = 0.2,
    seed: int = 42,
) -> dict[str, list[str]]:
    if abs(train_ratio + val_ratio - 1.0) > 1e-9:
        raise ValueError("train and val ratios must sum to 1")
    if min(train_ratio, val_ratio) <= 0:
        raise ValueError("split ratios cannot be negative")
    recordings = sorted(path.name for path in (source / "images").iterdir() if path.is_dir())
    required = 2
    if len(recordings) < required:
        raise ValueError(
            f"{len(recordings)} recording(s) cannot populate {required} non-empty splits; "
            "collect more recordings or set unused ratios to zero"
        )
    random.Random(seed).shuffle(recordings)
    counts = _split_counts(len(recordings), (train_ratio, val_ratio))
    assignments: dict[str, list[str]] = {}
    position = 0
    for split, count in zip(("train", "val"), counts):
        assignments[split] = recordings[position : position + count]
        position += count
    with staged_output(output) as staging:
        for split in ("train", "val"):
            (staging / "images" / split).mkdir(parents=True, exist_ok=True)
            (staging / "labels" / split).mkdir(parents=True, exist_ok=True)
        for split, split_recordings in assignments.items():
            for recording in split_recordings:
                for image in sorted((source / "images" / recording).iterdir()):
                    if not image.is_file():
                        continue
                    label = source / "labels" / recording / f"{image.stem}.txt"
                    if not label.is_file():
                        raise ValueError(f"generated image has no label: {image}")
                    destination_image = staging / "images" / split / f"{recording}--{image.name}"
                    destination_label = staging / "labels" / split / f"{recording}--{image.stem}.txt"
                    shutil.copy2(image, destination_image)
                    shutil.copy2(label, destination_label)
        shutil.copy2(source / "data.yaml", staging / "data.yaml")
        (staging / "split.json").write_text(
            json.dumps(assignments, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
    return assignments


def _split_counts(total: int, ratios: tuple[float, float]) -> tuple[int, int]:
    counts = [1, 1]
    remaining = total - sum(counts)
    while remaining:
        index = max(range(2), key=lambda item: ratios[item] * total - counts[item])
        counts[index] += 1
        remaining -= 1
    return counts[0], counts[1]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--train", type=float, default=0.8)
    parser.add_argument("--val", type=float, default=0.2)
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args(argv)
    try:
        assignments = split_by_recording(
            args.input,
            args.output,
            args.train,
            args.val,
            args.seed,
        )
    except (OSError, ValueError) as error:
        parser.error(str(error))
    print(" ".join(f"{name}={len(items)}" for name, items in assignments.items()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
