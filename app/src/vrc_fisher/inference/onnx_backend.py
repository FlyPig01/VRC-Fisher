"""Ultralytics-style YOLO inference implemented with ONNX Runtime and NumPy."""

from __future__ import annotations

import ast
import logging
from pathlib import Path
from typing import Any, Callable, Sequence

import numpy as np
from PIL import Image

from .backend import Detection


SessionFactory = Callable[[str, list[str]], Any]
CPU_PROVIDER = "CPUExecutionProvider"
DML_PROVIDER = "DmlExecutionProvider"


class ModelLoadError(RuntimeError):
    pass


class OnnxYoloModel:
    def __init__(
        self,
        path: str | Path,
        class_names: tuple[str, ...],
        confidence_threshold: float,
        iou_threshold: float,
        input_size: int = 640,
        intra_op_threads: int = 2,
        device: str = "auto",
        session_factory: SessionFactory | None = None,
        available_providers: Sequence[str] | None = None,
    ) -> None:
        self.path = Path(path)
        if not self.path.is_file():
            raise ModelLoadError(f"ONNX model not found: {self.path}")
        if input_size <= 0:
            raise ValueError("input_size must be positive")
        if intra_op_threads <= 0:
            raise ValueError("intra_op_threads must be positive")
        if not 0.0 <= confidence_threshold <= 1.0:
            raise ValueError("confidence_threshold must be between 0 and 1")
        if not 0.0 <= iou_threshold <= 1.0:
            raise ValueError("iou_threshold must be between 0 and 1")

        ort = None
        if session_factory is None:
            try:
                import onnxruntime as ort
            except ImportError as error:
                raise ModelLoadError("onnxruntime is not installed") from error
            available_providers = ort.get_available_providers()

        if available_providers is None:
            available_providers = (CPU_PROVIDER,)
        providers = _providers_for_device(device, available_providers)

        if session_factory is None:
            assert ort is not None
            options = ort.SessionOptions()
            options.intra_op_num_threads = intra_op_threads
            options.inter_op_num_threads = 1
            options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
            if providers[0] == DML_PROVIDER:
                options.enable_mem_pattern = False
                options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
                if device == "gpu":
                    options.add_session_config_entry("session.disable_cpu_ep_fallback", "1")
            session_factory = lambda model, providers: ort.InferenceSession(
                model, sess_options=options, providers=providers
            )

        try:
            self._session = session_factory(str(self.path), providers)
        except Exception as error:
            if device == "auto" and providers[0] == DML_PROVIDER:
                logging.getLogger("vrc_fisher").warning(
                    "DirectML initialization failed for %s; falling back to CPU: %s",
                    self.path,
                    error,
                )
                try:
                    providers = [CPU_PROVIDER]
                    self._session = session_factory(str(self.path), providers)
                except Exception as fallback_error:
                    raise ModelLoadError(
                        f"cannot load ONNX model {self.path} with DirectML or CPU: "
                        f"{fallback_error}"
                    ) from fallback_error
            else:
                raise ModelLoadError(f"cannot load ONNX model {self.path}: {error}") from error

        self.execution_provider = providers[0]
        logging.getLogger("vrc_fisher").info(
            "loaded model=%s requested_device=%s provider=%s",
            self.path,
            device,
            self.execution_provider,
        )

        inputs = self._session.get_inputs()
        if len(inputs) != 1:
            raise ModelLoadError(
                f"expected one model input in {self.path}, found {len(inputs)}"
            )
        self._input_name = inputs[0].name
        shape = inputs[0].shape
        self._input_height = _fixed_dimension(shape, 2, input_size)
        self._input_width = _fixed_dimension(shape, 3, input_size)
        self._class_names = class_names
        self._confidence_threshold = confidence_threshold
        self._iou_threshold = iou_threshold
        self._validate_metadata_names()

    def detect(self, image_bgr: np.ndarray) -> list[Detection]:
        tensor, scale, offset_x, offset_y = self._preprocess(image_bgr)
        outputs = self._session.run(None, {self._input_name: tensor})
        if not outputs:
            raise RuntimeError(f"model produced no output: {self.path}")
        boxes, scores, class_ids = self._decode(np.asarray(outputs[0]))
        if boxes.size == 0:
            return []

        image_height, image_width = image_bgr.shape[:2]
        boxes[:, [0, 2]] = (boxes[:, [0, 2]] - offset_x) / scale
        boxes[:, [1, 3]] = (boxes[:, [1, 3]] - offset_y) / scale
        boxes[:, [0, 2]] = boxes[:, [0, 2]].clip(0, image_width)
        boxes[:, [1, 3]] = boxes[:, [1, 3]].clip(0, image_height)

        keep = _classwise_nms(boxes, scores, class_ids, self._iou_threshold)
        detections: list[Detection] = []
        for index in keep:
            x1, y1, x2, y2 = boxes[index]
            class_id = int(class_ids[index])
            if class_id < 0 or class_id >= len(self._class_names) or x2 <= x1 or y2 <= y1:
                continue
            detections.append(
                Detection(
                    self._class_names[class_id],
                    float(scores[index]),
                    (int(round(x1)), int(round(y1)), int(round(x2)), int(round(y2))),
                )
            )
        return sorted(detections, key=lambda item: item.confidence, reverse=True)

    def _preprocess(
        self,
        image_bgr: np.ndarray,
    ) -> tuple[np.ndarray, float, int, int]:
        if image_bgr.ndim != 3 or image_bgr.shape[2] < 3:
            raise ValueError("expected a BGR image with three channels")
        source_height, source_width = image_bgr.shape[:2]
        scale = min(self._input_width / source_width, self._input_height / source_height)
        resized_width = max(1, int(round(source_width * scale)))
        resized_height = max(1, int(round(source_height * scale)))
        offset_x = (self._input_width - resized_width) // 2
        offset_y = (self._input_height - resized_height) // 2

        rgb = image_bgr[:, :, :3][:, :, ::-1]
        resized = np.asarray(
            Image.fromarray(rgb).resize(
                (resized_width, resized_height),
                Image.Resampling.BILINEAR,
            ),
            dtype=np.uint8,
        )
        canvas = np.full((self._input_height, self._input_width, 3), 114, dtype=np.uint8)
        canvas[offset_y : offset_y + resized_height, offset_x : offset_x + resized_width] = resized
        tensor = np.ascontiguousarray(canvas.transpose(2, 0, 1)[None], dtype=np.float32)
        tensor /= 255.0
        return tensor, scale, offset_x, offset_y

    def _decode(self, output: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
        values = np.squeeze(output)
        if values.ndim == 1 and values.size == 6:
            values = values.reshape(1, 6)
        elif values.ndim == 1 and values.size in {
            4 + len(self._class_names),
            5 + len(self._class_names),
        }:
            values = values.reshape(1, -1)
        if values.ndim != 2:
            raise RuntimeError(
                f"unsupported output shape {output.shape} from {self.path}; expected YOLO detections"
            )

        # Export with nms=True produces rows of x1,y1,x2,y2,confidence,class.
        if values.shape[1] == 6:
            boxes = values[:, :4].astype(np.float32, copy=True)
            scores = values[:, 4].astype(np.float32, copy=False)
            class_ids = values[:, 5].astype(np.int64, copy=False)
            selected = scores >= self._confidence_threshold
            return boxes[selected], scores[selected], class_ids[selected]

        expected_without_objectness = 4 + len(self._class_names)
        expected_with_objectness = 5 + len(self._class_names)
        if values.shape[0] in {expected_without_objectness, expected_with_objectness}:
            values = values.T
        elif values.shape[1] not in {expected_without_objectness, expected_with_objectness}:
            # Ultralytics commonly exports [C, N] after squeezing the batch
            # dimension. Convert that layout to one detection per row.
            if values.shape[0] < values.shape[1] and values.shape[0] <= 128:
                values = values.T
        if values.shape[1] not in {expected_without_objectness, expected_with_objectness}:
            raise RuntimeError(
                f"unsupported output shape {output.shape} from {self.path}; "
                f"expected {expected_without_objectness} or {expected_with_objectness} values per box"
            )

        class_start = 4 if values.shape[1] == expected_without_objectness else 5
        class_scores = values[:, class_start:]
        class_ids = np.argmax(class_scores, axis=1)
        scores = class_scores[np.arange(len(values)), class_ids]
        if class_start == 5:
            scores = scores * values[:, 4]
        selected = scores >= self._confidence_threshold
        xywh = values[selected, :4].astype(np.float32, copy=False)
        boxes = np.empty_like(xywh)
        boxes[:, 0] = xywh[:, 0] - xywh[:, 2] / 2
        boxes[:, 1] = xywh[:, 1] - xywh[:, 3] / 2
        boxes[:, 2] = xywh[:, 0] + xywh[:, 2] / 2
        boxes[:, 3] = xywh[:, 1] + xywh[:, 3] / 2
        return boxes, scores[selected].astype(np.float32), class_ids[selected].astype(np.int64)

    def _validate_metadata_names(self) -> None:
        try:
            raw_names = self._session.get_modelmeta().custom_metadata_map.get("names")
        except Exception:
            return
        if not raw_names:
            return
        try:
            parsed = ast.literal_eval(raw_names)
            exported = tuple(parsed[index] for index in range(len(parsed)))
        except (ValueError, SyntaxError, KeyError, TypeError):
            return
        if exported != self._class_names:
            raise ModelLoadError(
                f"class names in {self.path} are {exported}, expected {self._class_names}"
            )


def _providers_for_device(device: str, available_providers: Sequence[str]) -> list[str]:
    if device not in {"auto", "cpu", "gpu"}:
        raise ValueError("device must be one of: auto, cpu, gpu")
    available = set(available_providers)
    if CPU_PROVIDER not in available:
        raise ModelLoadError("ONNX Runtime does not provide CPUExecutionProvider")
    if device == "cpu":
        return [CPU_PROVIDER]
    if DML_PROVIDER in available:
        return [DML_PROVIDER, CPU_PROVIDER] if device == "auto" else [DML_PROVIDER]
    if device == "gpu":
        raise ModelLoadError(
            "DirectML GPU provider is unavailable; install or download the DirectML build"
        )
    return [CPU_PROVIDER]


def _fixed_dimension(shape: list[Any], index: int, fallback: int) -> int:
    if len(shape) > index and isinstance(shape[index], int) and shape[index] > 0:
        return shape[index]
    return fallback


def _classwise_nms(
    boxes: np.ndarray,
    scores: np.ndarray,
    class_ids: np.ndarray,
    threshold: float,
) -> list[int]:
    kept: list[int] = []
    for class_id in np.unique(class_ids):
        candidates = np.flatnonzero(class_ids == class_id)
        order = candidates[np.argsort(scores[candidates])[::-1]]
        while order.size:
            current = int(order[0])
            kept.append(current)
            if order.size == 1:
                break
            remaining = order[1:]
            order = remaining[_iou(boxes[current], boxes[remaining]) <= threshold]
    kept.sort(key=lambda index: float(scores[index]), reverse=True)
    return kept


def _iou(box: np.ndarray, others: np.ndarray) -> np.ndarray:
    intersection_x1 = np.maximum(box[0], others[:, 0])
    intersection_y1 = np.maximum(box[1], others[:, 1])
    intersection_x2 = np.minimum(box[2], others[:, 2])
    intersection_y2 = np.minimum(box[3], others[:, 3])
    intersection = np.maximum(0, intersection_x2 - intersection_x1) * np.maximum(
        0, intersection_y2 - intersection_y1
    )
    box_area = max(0.0, float(box[2] - box[0])) * max(0.0, float(box[3] - box[1]))
    other_areas = np.maximum(0, others[:, 2] - others[:, 0]) * np.maximum(
        0, others[:, 3] - others[:, 1]
    )
    return intersection / np.maximum(box_area + other_areas - intersection, 1e-9)
