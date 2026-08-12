"""Runtime orchestration for live screen capture and automatic input."""

from __future__ import annotations

import logging
from pathlib import Path
from time import monotonic, perf_counter, sleep

from PIL import Image

from vrc_fisher.capture.mss_source import MssSource
from vrc_fisher.config import AppConfig
from vrc_fisher.contracts import Action, Decision, Detector, InputSink
from vrc_fisher.inference import TwoStageOnnxDetector
from vrc_fisher.state.machine import FishingStateMachine
from vrc_fisher.telemetry.metrics import RuntimeMetrics
from vrc_fisher.window.win32 import GameWindow, emergency_stop_pressed


class FishingAutomation:
    def __init__(
        self,
        config: AppConfig,
        input_sink: InputSink,
        live_input: bool,
        artifacts_dir: Path,
        detector: Detector | None = None,
    ) -> None:
        self._config = config
        self._input = input_sink
        self._live_input = live_input
        self._artifacts_dir = artifacts_dir
        self._detector = detector or TwoStageOnnxDetector(config.vision)
        self._state = FishingStateMachine(config)
        self._window = GameWindow(config.window.title_contains)
        self._metrics = RuntimeMetrics()
        self._logger = logging.getLogger("vrc_fisher")
        self._last_activate = float("-inf")

    def run(self, max_seconds: float | None = None) -> None:
        started = monotonic()
        frame_period = 1.0 / self._config.capture.target_fps
        self._window.find()
        if self._live_input:
            self._activate_or_raise()
        self._logger.info(
            "started mode=%s monitor=%d window=%r emergency_stop=F8",
            "live" if self._live_input else "observe",
            self._config.capture.monitor,
            self._config.window.title_contains,
        )

        try:
            with MssSource(self._config.capture.monitor) as source:
                while True:
                    loop_started = perf_counter()
                    now = monotonic()
                    if max_seconds is not None and now - started >= max_seconds:
                        break
                    if emergency_stop_pressed():
                        self._logger.warning("F8 emergency stop")
                        break

                    frame = source.grab()
                    infer_started = perf_counter()
                    observation = self._detector.observe(frame)
                    inference_ms = (perf_counter() - infer_started) * 1000
                    decision = self._state.step(observation, now)
                    self._dispatch(decision, now)
                    self._metrics.record(frame, observation, inference_ms)

                    if decision.reason:
                        self._logger.info(
                            "cycle=%d phase=%s action=%s reason=%s confidence=%.2f",
                            decision.cycle,
                            decision.phase.value,
                            decision.action.value,
                            decision.reason,
                            observation.confidence,
                        )
                    if (
                        decision.phase.value == "recovery"
                        and decision.reason
                        and self._config.debug.save_failures
                    ):
                        self._save_failure(frame.image_bgr, decision)

                    remaining = frame_period - (perf_counter() - loop_started)
                    if remaining > 0:
                        sleep(remaining)
        finally:
            stopped = self._state.stop(monotonic())
            self._execute(stopped.action)
            self._metrics.write(self._artifacts_dir / "runtime-metrics.json")
            self._logger.info("stopped %s", self._metrics.summary())

    def _dispatch(self, decision: Decision, now: float) -> None:
        if decision.action is Action.NONE:
            return
        if self._live_input:
            if now - self._last_activate >= self._config.window.activate_interval_seconds:
                self._activate_or_raise()
            if not self._window.is_foreground():
                self._logger.error("input suppressed because VRChat is not foreground")
                self._input.release()
                return
        self._execute(decision.action)

    def _execute(self, action: Action) -> None:
        if action is Action.CLICK:
            self._input.click()
        elif action is Action.PRESS:
            self._input.press()
        elif action is Action.RELEASE:
            self._input.release()

    def _activate_or_raise(self) -> None:
        if not self._window.activate():
            raise RuntimeError("VRChat window could not be brought to foreground")
        self._last_activate = monotonic()

    def _save_failure(self, image, decision: Decision) -> None:
        directory = self._artifacts_dir / "failures"
        directory.mkdir(parents=True, exist_ok=True)
        path = directory / f"cycle-{decision.cycle:04d}-{int(monotonic() * 1000)}.jpg"
        Image.fromarray(image[:, :, ::-1]).save(path, format="JPEG", quality=90)
