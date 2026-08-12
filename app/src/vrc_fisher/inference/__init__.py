"""Inference backends and stage-specific detectors."""
"""Two-stage ONNX vision backend."""

from .pipeline import TwoStageOnnxDetector

__all__ = ["TwoStageOnnxDetector"]
