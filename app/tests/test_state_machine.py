from dataclasses import replace

from vrc_fisher.config import AppConfig, TimingConfig, VisionConfig
from vrc_fisher.contracts import Action, Observation, Phase
from vrc_fisher.state.machine import FishingStateMachine


def observation(
    *, prompt: bool = False, ui: bool = False, success: bool = False, failure: bool = False,
    target: float | None = None, top: float | None = None, bottom: float | None = None,
) -> Observation:
    return Observation(
        frame_sequence=0,
        observed_at_ns=0,
        prompt=(0, 0, 1, 1) if prompt else None,
        fishing_ui=(0, 0, 10, 100) if ui else None,
        success=(0, 0, 1, 1) if success else None,
        failure=(0, 0, 1, 1) if failure else None,
        target_y_norm=target,
        control_top_norm=top,
        control_bottom_norm=bottom,
        confidence=1.0,
    )


def fast_config() -> AppConfig:
    return AppConfig(
        vision=VisionConfig(
            prompt_confirm_frames=2,
            ui_confirm_frames=2,
            ui_lost_frames=2,
            success_confirm_frames=2,
            failure_confirm_frames=2,
        ),
        timing=TimingConfig(
            cast_settle_seconds=0.1,
            bite_timeout_seconds=1.0,
            hook_to_ui_min_seconds=0.1,
            prompt_to_ui_timeout_seconds=0.8,
            minigame_timeout_seconds=2.0,
            reward_min_wait_seconds=0.1,
            loot_timeout_seconds=1.0,
            cycle_delay_seconds=0.1,
            recovery_delay_seconds=0.1,
        ),
    )


def advance_to_minigame(machine: FishingStateMachine) -> None:
    assert machine.step(observation(), 0.0).action is Action.CLICK
    assert machine.step(observation(), 0.1).phase is Phase.WAITING_BITE
    machine.step(observation(prompt=True), 0.2)
    bite = machine.step(observation(prompt=True), 0.3)
    assert bite.phase is Phase.HOOKING
    assert bite.action is Action.CLICK
    machine.step(observation(ui=True), 0.4)
    started = machine.step(observation(ui=True), 0.5)
    assert started.phase is Phase.MINIGAME


def test_full_cycle_cast_hook_control_reel_pickup_and_restart() -> None:
    machine = FishingStateMachine(fast_config())
    advance_to_minigame(machine)

    press = machine.step(
        observation(ui=True, target=0.2, top=0.5, bottom=0.8), 0.6
    )
    assert press.action is Action.PRESS
    release = machine.step(
        observation(ui=True, target=0.9, top=0.4, bottom=0.7), 0.7
    )
    assert release.action is Action.RELEASE

    machine.step(observation(), 0.8)
    reel = machine.step(observation(), 0.9)
    assert reel.phase is Phase.LOOT
    assert reel.action is Action.CLICK

    machine.step(observation(success=True), 1.0)
    pickup = machine.step(observation(success=True), 1.1)
    assert pickup.action is Action.CLICK
    assert pickup.reason == "pick up reward"

    next_cycle = machine.step(observation(), 1.21)
    assert next_cycle.phase is Phase.IDLE
    cast = machine.step(observation(), 1.22)
    assert cast.action is Action.CLICK
    assert cast.cycle == 2


def test_minigame_end_releases_before_reel_when_mouse_is_held() -> None:
    machine = FishingStateMachine(fast_config())
    advance_to_minigame(machine)
    assert machine.step(
        observation(ui=True, target=0.2, top=0.5, bottom=0.8), 0.6
    ).action is Action.PRESS

    release = machine.step(observation(), 0.7)
    assert release.phase is Phase.MINIGAME
    assert release.action is Action.RELEASE
    reel = machine.step(observation(), 0.8)
    assert reel.phase is Phase.LOOT
    assert reel.action is Action.CLICK


def test_bite_timeout_enters_recovery_and_retries() -> None:
    machine = FishingStateMachine(fast_config())
    machine.step(observation(), 0.0)
    machine.step(observation(), 0.1)

    recovery = machine.step(observation(), 1.2)
    assert recovery.phase is Phase.RECOVERY
    assert recovery.action is Action.CLICK
    assert recovery.reason == "bite timeout"
    assert machine.step(observation(), 1.31).phase is Phase.IDLE
    assert machine.step(observation(), 1.32).action is Action.CLICK


def test_stop_releases_held_mouse() -> None:
    machine = FishingStateMachine(fast_config())
    advance_to_minigame(machine)
    machine.step(observation(ui=True, target=0.2, top=0.5, bottom=0.8), 0.6)

    decision = machine.stop(0.7)
    assert decision.phase is Phase.STOPPED
    assert decision.action is Action.RELEASE


def test_confirmed_failure_enters_recovery() -> None:
    machine = FishingStateMachine(fast_config())
    machine.step(observation(), 0.0)
    machine.step(observation(), 0.1)
    machine.step(observation(failure=True), 0.2)
    recovery = machine.step(observation(failure=True), 0.3)

    assert recovery.phase is Phase.RECOVERY
    assert recovery.action is Action.CLICK
    assert recovery.reason == "failure detected"
