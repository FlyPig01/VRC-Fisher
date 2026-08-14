"""Run both detectors on an unlabelled full-screen video for human review."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
from typing import Protocol

import av
import numpy as np
from PIL import Image, ImageDraw

from .environment import configure_ultralytics


@dataclass(frozen=True, slots=True)
class Detection:
    class_id: int
    confidence: float
    box: tuple[float, float, float, float]


class Detector(Protocol):
    def detect(self, image: np.ndarray, confidence: float) -> list[Detection]: ...


@dataclass(frozen=True, slots=True)
class FrameReview:
    locator_detections: int
    panels: int
    minigame_detections: int


def _clamp_box(
    box: tuple[float, float, float, float], width: int, height: int
) -> tuple[int, int, int, int] | None:
    left, top, right, bottom = box
    left = max(0, min(width, int(round(left))))
    top = max(0, min(height, int(round(top))))
    right = max(0, min(width, int(round(right))))
    bottom = max(0, min(height, int(round(bottom))))
    if right <= left or bottom <= top:
        return None
    return left, top, right, bottom


def panel_crop_box(
    panel: Detection, width: int, height: int, padding: float
) -> tuple[int, int, int, int] | None:
    if not 0.0 <= padding <= 0.5:
        raise ValueError("padding must be between 0 and 0.5")
    left, top, right, bottom = panel.box
    pad_x = (right - left) * padding
    pad_y = (bottom - top) * padding
    return _clamp_box((left - pad_x, top - pad_y, right + pad_x, bottom + pad_y), width, height)


def map_box_to_full_frame(
    box: tuple[float, float, float, float], crop: tuple[int, int, int, int]
) -> tuple[float, float, float, float]:
    left, top, right, bottom = crop
    return box[0] + left, box[1] + top, box[2] + left, box[3] + top


def review_frame(
    frame: np.ndarray,
    locator: Detector,
    minigame: Detector,
    confidence: float = 0.25,
    padding: float = 0.08,
) -> tuple[np.ndarray, FrameReview]:
    if frame.ndim != 3 or frame.shape[2] != 3:
        raise ValueError("frame must be an RGB image with shape HxWx3")
    if not 0.0 <= confidence <= 1.0:
        raise ValueError("confidence must be between 0 and 1")
    height, width = frame.shape[:2]
    locator_detections = locator.detect(frame, confidence)
    panels = [item for item in locator_detections if item.class_id == 1]
    image = Image.fromarray(frame, mode="RGB")
    draw = ImageDraw.Draw(image)
    _draw_detections(draw, locator_detections, (255, 90, 90), ("bite_indicator", "minigame_panel"))
    minigame_count = 0
    for panel in panels:
        crop = panel_crop_box(panel, width, height, padding)
        if crop is None:
            continue
        crop_left, crop_top, crop_right, crop_bottom = crop
        draw.rectangle(crop, outline=(255, 190, 40), width=max(2, min(width, height) // 400))
        local = frame[crop_top:crop_bottom, crop_left:crop_right]
        for detection in minigame.detect(local, confidence):
            full_box = map_box_to_full_frame(detection.box, crop)
            _draw_one(
                draw,
                Detection(detection.class_id, detection.confidence, full_box),
                (40, 220, 120),
                ("catch_zone", "moving_target"),
            )
            minigame_count += 1
    return np.asarray(image), FrameReview(len(locator_detections), len(panels), minigame_count)


def _draw_detections(draw: ImageDraw.ImageDraw, detections: list[Detection], color, names) -> None:
    for detection in detections:
        _draw_one(draw, detection, color, names)


def _draw_one(draw: ImageDraw.ImageDraw, detection: Detection, color, names) -> None:
    if not 0 <= detection.class_id < len(names):
        return
    left, top, right, bottom = detection.box
    width = 2
    draw.rectangle((left, top, right, bottom), outline=color, width=width)
    label = f"{names[detection.class_id]} {detection.confidence:.2f}"
    text_box = draw.textbbox((left, top), label)
    label_top = max(0, text_box[1] - 2)
    draw.rectangle((left, label_top, text_box[2] + 3, top + 1), fill=(0, 0, 0))
    draw.text((left + 1, label_top), label, fill=color)


class UltralyticsDetector:
    def __init__(self, model_path: Path, device: str, image_size: int):
        configure_ultralytics()
        try:
            from ultralytics import YOLO
        except ImportError as error:
            raise RuntimeError("training dependencies are missing; install ultralytics") from error
        self._model = YOLO(str(model_path))
        self._device = device
        self._image_size = image_size

    def detect(self, image: np.ndarray, confidence: float) -> list[Detection]:
        results = self._model.predict(
            source=image,
            conf=confidence,
            device=self._device,
            imgsz=self._image_size,
            verbose=False,
        )
        if not results:
            return []
        boxes = results[0].boxes
        if boxes is None:
            return []
        coordinates = boxes.xyxy.cpu().numpy()
        classes = boxes.cls.cpu().numpy().astype(int)
        scores = boxes.conf.cpu().numpy()
        return [
            Detection(int(class_id), float(score), tuple(float(value) for value in box))
            for box, class_id, score in zip(coordinates, classes, scores)
        ]


def review_video(
    input_path: Path,
    output_path: Path,
    locator_path: Path,
    minigame_path: Path,
    device: str = "cpu",
    confidence: float = 0.25,
    padding: float = 0.08,
    locator_image_size: int = 960,
    minigame_image_size: int = 640,
) -> tuple[int, int, int]:
    for path in (input_path, locator_path, minigame_path):
        if not path.is_file():
            raise FileNotFoundError(f"required review input not found: {path}")
    if min(locator_image_size, minigame_image_size) <= 0:
        raise ValueError("model image sizes must be positive")
    locator = UltralyticsDetector(locator_path, device, locator_image_size)
    minigame = UltralyticsDetector(minigame_path, device, minigame_image_size)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    frames = locator_count = minigame_count = 0
    with av.open(str(input_path)) as source, av.open(str(output_path), mode="w") as destination:
        stream = next((item for item in source.streams if item.type == "video"), None)
        if stream is None:
            raise ValueError(f"video stream not found: {input_path}")
        rate = stream.average_rate or stream.guessed_rate or 30
        output_stream = destination.add_stream("libx264", rate=rate)
        output_stream.width = stream.codec_context.width
        output_stream.height = stream.codec_context.height
        output_stream.pix_fmt = "yuv420p"
        for decoded in source.decode(stream):
            frame = decoded.to_ndarray(format="rgb24")
            annotated, report = review_frame(frame, locator, minigame, confidence, padding)
            packet_frame = av.VideoFrame.from_ndarray(annotated, format="rgb24")
            for packet in output_stream.encode(packet_frame):
                destination.mux(packet)
            frames += 1
            locator_count += report.locator_detections
            minigame_count += report.minigame_detections
        for packet in output_stream.encode():
            destination.mux(packet)
    return frames, locator_count, minigame_count


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--locator", type=Path, default=Path("weights/locator.pt"), help="locator YOLO model")
    parser.add_argument("--minigame", type=Path, default=Path("weights/minigame.pt"), help="minigame YOLO model")
    parser.add_argument("--device", default="cpu", help="Ultralytics device, for example cpu or 0")
    parser.add_argument("--confidence", type=float, default=0.25)
    parser.add_argument("--padding", type=float, default=0.08)
    parser.add_argument("--locator-image-size", type=int, default=960)
    parser.add_argument("--minigame-image-size", type=int, default=640)
    args = parser.parse_args(argv)
    output = args.output or Path("test/results") / f"{args.input.stem}-review.mp4"
    try:
        frames, locator_count, minigame_count = review_video(
            args.input,
            output,
            args.locator,
            args.minigame,
            args.device,
            args.confidence,
            args.padding,
            args.locator_image_size,
            args.minigame_image_size,
        )
    except (FileNotFoundError, OSError, ValueError, RuntimeError) as error:
        parser.error(str(error))
    print(
        f"frames={frames} locator_detections={locator_count} "
        f"minigame_detections={minigame_count} output={output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
