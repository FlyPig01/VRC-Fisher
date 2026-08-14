from __future__ import annotations

from pathlib import Path

import numpy as np
from vrc_training.annotation_package import Thresholds, prelabel_frame, yolo_payload
from vrc_training.video_review import Detection


class FakeDetector:
    def __init__(self, detections: list[Detection]):
        self.detections = detections
        self.confidences: list[float] = []

    def detect(self, image: np.ndarray, confidence: float) -> list[Detection]:
        self.confidences.append(confidence)
        return [item for item in self.detections if item.confidence >= confidence]


def test_prelabelling_maps_local_minigame_boxes_back_to_full_frame() -> None:
    frame = np.zeros((100, 200, 3), dtype=np.uint8)
    locator = FakeDetector(
        [
            Detection(0, 0.18, (10, 10, 20, 30)),
            Detection(1, 0.90, (50, 20, 150, 80)),
        ]
    )
    minigame = FakeDetector(
        [
            Detection(0, 0.80, (20, 10, 60, 40)),
            Detection(1, 0.10, (30, 20, 40, 30)),
        ]
    )

    labels = prelabel_frame(frame, locator, minigame, Thresholds(), padding=0.0)

    assert labels == [
        ("bite_indicator", (10, 10, 20, 30)),
        ("minigame_panel", (50, 20, 150, 80)),
        ("catch_zone", (70, 30, 110, 60)),
        ("moving_target", (80, 40, 90, 50)),
    ]
    assert locator.confidences == [0.15]
    assert minigame.confidences == [0.05]


def test_yolo_payload_clamps_boxes_and_uses_project_class_ids() -> None:
    payload = yolo_payload(
        [("bite_indicator", (-1.2, 2.4, 10.6, 20.5))], 100, 80
    )

    assert payload == [(0, 0.053, 0.143125, 0.106, 0.22625)]


def test_yolo_payload_keeps_quantized_edge_boxes_inside_image() -> None:
    payload = yolo_payload(
        [("minigame_panel", (10.0, 0.0, 20.0, 52.9796024))], 100, 80
    )

    assert payload == [(1, 0.15, 0.33112252, 0.1, 0.66224503)]
    _, x_center, y_center, width, height = payload[0]
    serialized = [
        float(value)
        for value in f"{x_center:.8f} {y_center:.8f} {width:.8f} {height:.8f}".split()
    ]
    x_center, y_center, width, height = serialized
    assert x_center - width / 2 >= 0
    assert y_center - height / 2 >= 0
    assert x_center + width / 2 <= 1
    assert y_center + height / 2 <= 1
