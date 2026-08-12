from time import perf_counter_ns

import numpy as np

from vrc_fisher.config import VisionConfig
from vrc_fisher.contracts import Frame
from vrc_fisher.inference.backend import Detection
from vrc_fisher.inference.pipeline import TwoStageOnnxDetector


class FakeModel:
    def __init__(self, detections):
        self.detections = detections
        self.images = []

    def detect(self, image_bgr):
        self.images.append(image_bgr)
        return self.detections


def test_two_stage_detector_crops_and_normalizes_to_rail(tmp_path) -> None:
    locator = FakeModel(
        [
            Detection("fishing_ui_group", 0.9, (40, 20, 60, 80)),
            Detection("prompt", 0.8, (2, 3, 8, 14)),
        ]
    )
    minigame = FakeModel(
        [
            Detection("rail", 0.9, (8, 6, 14, 54)),
            Detection("control_bar", 0.8, (8, 30, 14, 42)),
            Detection("target", 0.7, (8, 12, 14, 18)),
            Detection("progress_bar", 0.6, (1, 20, 4, 44)),
        ]
    )
    config = VisionConfig(crop_padding=0.1)
    detector = TwoStageOnnxDetector(config, tmp_path, locator, minigame)
    frame = Frame(4, perf_counter_ns(), np.zeros((100, 100, 3), dtype=np.uint8))

    observation = detector.observe(frame)

    assert observation.fishing_ui == (40, 20, 60, 80)
    assert observation.prompt == (2, 3, 8, 14)
    assert minigame.images[0].shape[:2] == (72, 24)
    assert observation.target_y_norm == 0.1875
    assert observation.control_top_norm == 0.5
    assert observation.control_bottom_norm == 0.75
    assert observation.progress_norm == 0.5


def test_local_model_is_not_run_without_fishing_ui(tmp_path) -> None:
    locator = FakeModel([Detection("success", 0.9, (1, 1, 2, 2))])
    minigame = FakeModel([])
    detector = TwoStageOnnxDetector(VisionConfig(), tmp_path, locator, minigame)

    observation = detector.observe(
        Frame(0, perf_counter_ns(), np.zeros((20, 20, 3), dtype=np.uint8))
    )

    assert observation.has_success
    assert not observation.has_fishing_ui
    assert minigame.images == []


def test_locator_is_throttled_while_local_model_runs_each_frame(tmp_path) -> None:
    locator = FakeModel([Detection("fishing_ui_group", 0.9, (2, 2, 18, 18))])
    minigame = FakeModel([])
    detector = TwoStageOnnxDetector(
        VisionConfig(locator_interval_frames=3),
        tmp_path,
        locator,
        minigame,
    )
    image = np.zeros((20, 20, 3), dtype=np.uint8)

    for sequence in range(4):
        detector.observe(Frame(sequence, perf_counter_ns(), image))

    assert len(locator.images) == 2
    assert len(minigame.images) == 4
