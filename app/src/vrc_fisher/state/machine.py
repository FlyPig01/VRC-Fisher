"""Deterministic fishing state machine with timeout recovery."""

from __future__ import annotations

from dataclasses import dataclass

from vrc_fisher.config import AppConfig
from vrc_fisher.contracts import Action, Decision, Observation, Phase


@dataclass(slots=True)
class _Evidence:
    prompt: int = 0
    ui: int = 0
    ui_lost: int = 0
    success: int = 0
    failure: int = 0

    def update(self, observation: Observation) -> None:
        self.prompt = self.prompt + 1 if observation.has_prompt else 0
        self.ui = self.ui + 1 if observation.has_fishing_ui else 0
        self.ui_lost = self.ui_lost + 1 if not observation.has_fishing_ui else 0
        self.success = self.success + 1 if observation.has_success else 0
        self.failure = self.failure + 1 if observation.has_failure else 0

    def clear(self) -> None:
        self.prompt = self.ui = self.ui_lost = self.success = self.failure = 0


class FishingStateMachine:
    def __init__(self, config: AppConfig) -> None:
        self._config = config
        self._phase = Phase.IDLE
        self._entered_at = 0.0
        self._cycle = 0
        self._evidence = _Evidence()
        self._mouse_pressed = False
        self._loot_clicked = False

    @property
    def phase(self) -> Phase:
        return self._phase

    @property
    def cycle(self) -> int:
        return self._cycle

    def stop(self, now: float) -> Decision:
        action = Action.RELEASE if self._mouse_pressed else Action.NONE
        self._mouse_pressed = False
        self._transition(Phase.STOPPED, now)
        return Decision(self._phase, action, "stop requested", self._cycle)

    def step(self, observation: Observation, now: float) -> Decision:
        self._evidence.update(observation)

        if self._evidence.failure >= self._config.vision.failure_confirm_frames and self._phase not in {
            Phase.IDLE,
            Phase.CASTING,
            Phase.RECOVERY,
            Phase.STOPPED,
        }:
            return self._recover(now, "failure detected")

        if self._phase is Phase.IDLE:
            self._cycle += 1
            self._transition(Phase.CASTING, now)
            return Decision(self._phase, Action.CLICK, "cast", self._cycle)

        elapsed = now - self._entered_at
        timing = self._config.timing
        vision = self._config.vision

        if self._phase is Phase.CASTING:
            if elapsed >= timing.cast_settle_seconds:
                self._transition(Phase.WAITING_BITE, now)
                return Decision(self._phase, reason="cast settled", cycle=self._cycle)

        elif self._phase is Phase.WAITING_BITE:
            if self._evidence.prompt >= vision.prompt_confirm_frames:
                self._transition(Phase.HOOKING, now)
                return Decision(self._phase, Action.CLICK, "bite confirmed", self._cycle)
            if elapsed >= timing.bite_timeout_seconds:
                return self._recover(now, "bite timeout")

        elif self._phase is Phase.HOOKING:
            if (
                elapsed >= timing.hook_to_ui_min_seconds
                and self._evidence.ui >= vision.ui_confirm_frames
            ):
                self._transition(Phase.MINIGAME, now)
                return Decision(self._phase, reason="minigame confirmed", cycle=self._cycle)
            if elapsed >= timing.prompt_to_ui_timeout_seconds:
                return self._recover(now, "minigame did not start")

        elif self._phase is Phase.MINIGAME:
            if self._evidence.ui_lost >= vision.ui_lost_frames:
                release = self._mouse_pressed
                self._mouse_pressed = False
                if release:
                    self._transition(Phase.REELING, now)
                    return Decision(
                        self._phase,
                        Action.RELEASE,
                        "release before reeling",
                        self._cycle,
                    )
                self._transition(Phase.LOOT, now)
                return Decision(self._phase, Action.CLICK, "reel catch", self._cycle)
            if elapsed >= timing.minigame_timeout_seconds:
                return self._recover(now, "minigame timeout")
            action = self._minigame_action(observation)
            return Decision(
                self._phase,
                action,
                "raise control bar" if action is Action.PRESS else "lower control bar" if action is Action.RELEASE else "",
                self._cycle,
            )

        elif self._phase is Phase.REELING:
            self._transition(Phase.LOOT, now)
            return Decision(self._phase, Action.CLICK, "reel after release", self._cycle)

        elif self._phase is Phase.LOOT:
            if (
                elapsed >= timing.reward_min_wait_seconds
                and self._evidence.success >= vision.success_confirm_frames
                and not self._loot_clicked
            ):
                self._loot_clicked = True
                self._entered_at = now
                return Decision(self._phase, Action.CLICK, "pick up reward", self._cycle)
            if self._loot_clicked and elapsed >= timing.cycle_delay_seconds:
                self._transition(Phase.IDLE, now)
                return Decision(self._phase, reason="start next cycle", cycle=self._cycle)
            if elapsed >= timing.loot_timeout_seconds:
                self._transition(Phase.IDLE, now)
                return Decision(self._phase, reason="start next cycle", cycle=self._cycle)

        elif self._phase is Phase.RECOVERY:
            if elapsed >= timing.recovery_delay_seconds:
                self._transition(Phase.IDLE, now)
                return Decision(self._phase, reason="recovery complete", cycle=self._cycle)

        return Decision(self._phase, cycle=self._cycle)

    def _minigame_action(self, observation: Observation) -> Action:
        target = observation.target_y_norm
        top = observation.control_top_norm
        bottom = observation.control_bottom_norm
        if target is None or top is None or bottom is None:
            if self._mouse_pressed:
                self._mouse_pressed = False
                return Action.RELEASE
            return Action.NONE

        deadband = self._config.control.vertical_deadband
        center = (top + bottom) / 2
        # In this UI holding the button raises the white control region. Screen
        # y grows downward, so press while the target is above the bar center.
        if target < center - deadband and not self._mouse_pressed:
            self._mouse_pressed = True
            return Action.PRESS
        if target > center + deadband and self._mouse_pressed:
            self._mouse_pressed = False
            return Action.RELEASE
        return Action.NONE

    def _recover(self, now: float, reason: str) -> Decision:
        action = Action.RELEASE if self._mouse_pressed else Action.CLICK
        self._mouse_pressed = False
        self._transition(Phase.RECOVERY, now)
        return Decision(self._phase, action, reason, self._cycle)

    def _transition(self, phase: Phase, now: float) -> None:
        self._phase = phase
        self._entered_at = now
        self._evidence.clear()
        if phase is not Phase.LOOT:
            self._loot_clicked = False
