from __future__ import annotations

import numpy as np

from vrc_training.video_review import (
    Detection,
    map_box_to_full_frame,
    panel_crop_box,
    review_frame,
)


class FakeDetector:
    def __init__(self, detections: list[Detection]) -> None:
        self.detections = detections
        self.calls: list[tuple[int, int]] = []

    def detect(self, image: np.ndarray, confidence: float) -> list[Detection]:
        self.calls.append((image.shape[1], image.shape[0]))
        return list(self.detections)


def test_panel_crop_box_adds_padding_and_clamps_to_frame() -> None:
    panel = Detection(1, 0.9, (10, 20, 90, 80))

    assert panel_crop_box(panel, 100, 100, 0.1) == (2, 14, 98, 86)


def test_map_box_to_full_frame_offsets_local_coordinates() -> None:
    assert map_box_to_full_frame((4, 5, 20, 25), (10, 30, 80, 90)) == (
        14,
        35,
        30,
        55,
    )


def test_review_frame_crops_panel_and_maps_minigame_boxes() -> None:
    frame = np.zeros((100, 120, 3), dtype=np.uint8)
    locator = FakeDetector([Detection(1, 0.95, (20, 10, 80, 70))])
    minigame = FakeDetector([Detection(0, 0.8, (5, 6, 25, 26))])

    annotated, report = review_frame(frame, locator, minigame, padding=0)

    assert annotated.shape == frame.shape
    assert report.locator_detections == 1
    assert report.panels == 1
    assert report.minigame_detections == 1
    assert locator.calls == [(120, 100)]
    assert minigame.calls == [(60, 60)]
    # A non-black pixel proves the returned frame contains a drawn detection.
    assert np.any(annotated != 0)


def test_review_frame_skips_minigame_without_panel() -> None:
    frame = np.zeros((32, 48, 3), dtype=np.uint8)
    locator = FakeDetector([Detection(0, 0.9, (1, 1, 8, 8))])
    minigame = FakeDetector([])

    _, report = review_frame(frame, locator, minigame)

    assert report.panels == 0
    assert report.minigame_detections == 0
    assert minigame.calls == []
