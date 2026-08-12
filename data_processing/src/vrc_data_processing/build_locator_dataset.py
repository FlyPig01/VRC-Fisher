"""Build the full-screen locator dataset from reviewed full-screen labels."""

from __future__ import annotations

import argparse
from pathlib import Path
import shutil

from .labels import LOCATOR_CLASS_IDS, Label, annotation_path, frame_files, read_labels, write_labels


def build_locator_dataset(frames: Path, annotations: Path, output: Path) -> tuple[int, int]:
    images_written = 0
    labels_written = 0
    for frame in sorted(frame_files(frames)):
        source_label = annotation_path(annotations, frame)
        if not source_label.is_file():
            continue
        labels = [
            Label(label.class_id, label.x_center, label.y_center, label.width, label.height)
            for label in read_labels(source_label)
            if label.class_id in LOCATOR_CLASS_IDS
        ]
        destination_image = output / "images" / frame.parent.name / frame.name
        destination_label = output / "labels" / frame.parent.name / f"{frame.stem}.txt"
        destination_image.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(frame, destination_image)
        write_labels(destination_label, labels)
        images_written += 1
        labels_written += len(labels)
    _write_yaml(output, ("prompt", "fishing_ui_group", "success", "failure"))
    return images_written, labels_written


def _write_yaml(output: Path, names: tuple[str, ...]) -> None:
    lines = ["path: .", "train: images/train", "val: images/val", "test: images/test", "names:"]
    lines.extend(f"  {index}: {name}" for index, name in enumerate(names))
    output.mkdir(parents=True, exist_ok=True)
    (output / "data.yaml").write_text("\n".join(lines) + "\n", encoding="ascii")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--frames", type=Path, default=Path("work/frames"))
    parser.add_argument("--annotations", type=Path, default=Path("input/annotations"))
    parser.add_argument("--output", type=Path, default=Path("output/locator"))
    args = parser.parse_args(argv)
    images, labels = build_locator_dataset(args.frames, args.annotations, args.output)
    if images == 0:
        parser.error("no annotated frames found; run the annotation audit and add reviewed labels")
    print(f"images={images} labels={labels} output={args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
