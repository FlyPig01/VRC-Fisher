"""YOLO label parsing and coordinate conversion shared by dataset builders."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path


ALL_CLASSES = (
    "prompt",
    "fishing_ui_group",
    "success",
    "failure",
    "rail",
    "control_bar",
    "target",
    "progress_bar",
)
LOCATOR_CLASS_IDS = range(4)
MINIGAME_CLASS_IDS = range(4, 8)


@dataclass(frozen=True, slots=True)
class Label:
    class_id: int
    x_center: float
    y_center: float
    width: float
    height: float

    def validate(self) -> None:
        if not 0 <= self.class_id < len(ALL_CLASSES):
            raise ValueError(f"unknown class id {self.class_id}")
        for name, value in (
            ("x_center", self.x_center),
            ("y_center", self.y_center),
            ("width", self.width),
            ("height", self.height),
        ):
            if not 0.0 <= value <= 1.0:
                raise ValueError(f"{name} must be between 0 and 1, got {value}")
        if self.width <= 0 or self.height <= 0:
            raise ValueError("label width and height must be positive")

    def pixels(self, image_width: int, image_height: int) -> tuple[float, float, float, float]:
        half_width = self.width * image_width / 2
        half_height = self.height * image_height / 2
        center_x = self.x_center * image_width
        center_y = self.y_center * image_height
        return (
            center_x - half_width,
            center_y - half_height,
            center_x + half_width,
            center_y + half_height,
        )


def read_labels(path: Path) -> list[Label]:
    labels: list[Label] = []
    if not path.is_file():
        return labels
    for line_number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        stripped = raw.strip()
        if not stripped:
            continue
        parts = stripped.split()
        if len(parts) != 5:
            raise ValueError(f"{path}:{line_number}: expected 5 YOLO fields")
        try:
            label = Label(int(parts[0]), *(float(value) for value in parts[1:]))
        except ValueError as error:
            raise ValueError(f"{path}:{line_number}: invalid YOLO label") from error
        label.validate()
        labels.append(label)
    return labels


def write_labels(path: Path, labels: list[Label]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    content = "".join(
        f"{label.class_id} {label.x_center:.8f} {label.y_center:.8f} "
        f"{label.width:.8f} {label.height:.8f}\n"
        for label in labels
    )
    path.write_text(content, encoding="ascii")


def frame_files(root: Path):
    for extension in ("*.jpg", "*.jpeg", "*.png"):
        yield from root.glob(f"*/{extension}")


def annotation_path(annotations: Path, frame: Path) -> Path:
    return annotations / frame.parent.name / f"{frame.stem}.txt"
