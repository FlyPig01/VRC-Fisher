import numpy as np

from pathlib import Path

from vrc_fisher.inference.onnx_backend import OnnxYoloModel, _classwise_nms


def test_nms_suppresses_overlapping_boxes_of_the_same_class() -> None:
    boxes = np.array(
        [[0, 0, 10, 10], [1, 1, 11, 11], [1, 1, 11, 11]],
        dtype=np.float32,
    )
    scores = np.array([0.9, 0.8, 0.7], dtype=np.float32)
    classes = np.array([0, 0, 1], dtype=np.int64)

    assert _classwise_nms(boxes, scores, classes, 0.5) == [0, 2]


def test_decodes_ultralytics_raw_output() -> None:
    model = OnnxYoloModel.__new__(OnnxYoloModel)
    model.path = Path("model.onnx")
    model._class_names = ("a", "b", "c", "d")
    model._confidence_threshold = 0.35
    output = np.array(
        [
            [
                [10.0, 30.0],
                [20.0, 40.0],
                [4.0, 8.0],
                [6.0, 10.0],
                [0.9, 0.1],
                [0.1, 0.8],
                [0.0, 0.0],
                [0.0, 0.0],
            ]
        ],
        dtype=np.float32,
    )

    boxes, scores, classes = model._decode(output)

    np.testing.assert_allclose(boxes, [[8, 17, 12, 23], [26, 35, 34, 45]])
    np.testing.assert_allclose(scores, [0.9, 0.8])
    np.testing.assert_array_equal(classes, [0, 1])


def test_decodes_batched_channel_first_raw_output() -> None:
    model = OnnxYoloModel.__new__(OnnxYoloModel)
    model.path = Path("model.onnx")
    model._class_names = ("a", "b", "c", "d")
    model._confidence_threshold = 0.35
    output = np.zeros((1, 8, 2), dtype=np.float32)
    output[0, :, 0] = [10.0, 20.0, 4.0, 6.0, 0.9, 0.1, 0.0, 0.0]
    output[0, :, 1] = [30.0, 40.0, 8.0, 10.0, 0.1, 0.8, 0.0, 0.0]

    boxes, scores, classes = model._decode(output)

    np.testing.assert_allclose(boxes, [[8, 17, 12, 23], [26, 35, 34, 45]])
    np.testing.assert_allclose(scores, [0.9, 0.8])
    np.testing.assert_array_equal(classes, [0, 1])
