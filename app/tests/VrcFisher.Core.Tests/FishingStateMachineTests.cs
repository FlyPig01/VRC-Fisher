using VrcFisher.Core;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class FishingStateMachineTests
{
    private static readonly BoundingBox Panel = new(0, 0, 100, 100);
    private static readonly BoundingBox Zone = new(10, 40, 30, 60);

    [Fact]
    public void First_step_casts_and_requires_fresh_prompt_evidence()
    {
        var machine = new FishingStateMachine(StateMachineOptions.Default);
        var start = DateTimeOffset.UtcNow;

        var cast = machine.Step(new DetectionObservation(1, start), start);
        Assert.Equal(FishingPhase.Casting, cast.Phase);
        Assert.Equal(InputAction.Click, cast.Action);

        var waiting = machine.Step(new DetectionObservation(2, start), start.AddSeconds(1));
        Assert.Equal(FishingPhase.WaitingForBite, waiting.Phase);

        var onePrompt = machine.Step(
            new DetectionObservation(3, start, BiteIndicator: new BoundingBox(1, 1, 2, 2)),
            start.AddSeconds(1.1));
        Assert.Equal(FishingPhase.WaitingForBite, onePrompt.Phase);
        Assert.Equal(InputAction.None, onePrompt.Action);
    }

    [Fact]
    public void Animated_indicator_can_be_confirmed_with_nonconsecutive_hits()
    {
        var options = StateMachineOptions.Default with
        {
            BiteIndicatorConfirmFrames = 3,
            BiteIndicatorEvidenceWindow = 5
        };
        var machine = new FishingStateMachine(options);
        var now = DateTimeOffset.UtcNow;
        machine.Step(new DetectionObservation(1, now), now);
        machine.Step(new DetectionObservation(2, now), now.AddSeconds(1));

        var indicator = new BoundingBox(0, 0, 10, 10);
        machine.Step(new DetectionObservation(3, now, BiteIndicator: indicator), now.AddSeconds(1.1));
        machine.Step(new DetectionObservation(4, now), now.AddSeconds(1.2));
        machine.Step(new DetectionObservation(5, now, BiteIndicator: indicator), now.AddSeconds(1.3));
        machine.Step(new DetectionObservation(6, now), now.AddSeconds(1.4));
        var confirmed = machine.Step(
            new DetectionObservation(7, now, BiteIndicator: indicator),
            now.AddSeconds(1.5));

        Assert.Equal(FishingPhase.Hooking, confirmed.Phase);
        Assert.Equal(InputAction.Click, confirmed.Action);
    }

    [Fact]
    public void Stop_is_idempotent()
    {
        var machine = new FishingStateMachine(StateMachineOptions.Default);
        var now = DateTimeOffset.UtcNow;
        machine.Step(new DetectionObservation(1, now), now);

        var stop = machine.Stop(now.AddSeconds(1));
        var second = machine.Stop(now.AddSeconds(2));

        Assert.Equal(FishingPhase.Stopped, stop.Phase);
        Assert.Equal(InputAction.None, stop.Action);
        Assert.Equal(InputAction.None, second.Action);
    }

    [Fact]
    public void Reset_after_stop_starts_a_new_cycle()
    {
        var machine = new FishingStateMachine(StateMachineOptions.Default);
        var now = DateTimeOffset.UtcNow;
        machine.Stop(now);

        machine.Reset(now.AddSeconds(1));
        var decision = machine.Step(
            new DetectionObservation(1, now.AddSeconds(1)),
            now.AddSeconds(1));

        Assert.Equal(FishingPhase.Casting, decision.Phase);
        Assert.Equal(InputAction.Click, decision.Action);
    }

    [Fact]
    public void Missing_target_requests_release()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);

        var decision = machine.Step(new DetectionObservation(
            5,
            now.AddSeconds(1.4),
            MinigamePanel: Panel,
            PanelGeneration: 1), now.AddSeconds(1.4));

        Assert.Equal(FishingPhase.Minigame, decision.Phase);
        Assert.Equal(InputAction.Release, decision.Action);
    }

    [Fact]
    public void Bite_timeout_enters_recovery_without_an_extra_click()
    {
        var options = StateMachineOptions.Default with { BiteTimeout = TimeSpan.FromSeconds(1) };
        var machine = new FishingStateMachine(options);
        var now = DateTimeOffset.UtcNow;
        machine.Step(new DetectionObservation(1, now), now);
        machine.Step(new DetectionObservation(2, now), now.AddSeconds(1));

        var timeout = machine.Step(new DetectionObservation(3, now), now.AddSeconds(2.1));

        Assert.Equal(FishingPhase.Recovery, timeout.Phase);
        Assert.Equal(InputAction.None, timeout.Action);
    }

    [Fact]
    public void Bite_fallback_clicks_once_after_the_configured_delay()
    {
        var options = StateMachineOptions.Default with
        {
            BiteFallback = TimeSpan.FromSeconds(2),
            BiteTimeout = TimeSpan.FromSeconds(10)
        };
        var machine = new FishingStateMachine(options);
        var now = DateTimeOffset.UtcNow;
        machine.Step(new DetectionObservation(1, now), now);
        machine.Step(new DetectionObservation(2, now), now.AddSeconds(1));

        var fallback = machine.Step(new DetectionObservation(3, now), now.AddSeconds(3.1));
        var afterFallback = machine.Step(new DetectionObservation(4, now), now.AddSeconds(3.2));

        Assert.Equal(FishingPhase.Hooking, fallback.Phase);
        Assert.Equal(InputAction.Click, fallback.Action);
        Assert.Equal(InputAction.None, afterFallback.Action);
    }

    [Fact]
    public void Failed_bite_fallback_returns_to_waiting_without_a_second_click()
    {
        var options = StateMachineOptions.Default with
        {
            BiteFallback = TimeSpan.FromSeconds(2),
            BiteToMinigameTimeout = TimeSpan.FromSeconds(1),
            RecoveryDelay = TimeSpan.FromSeconds(1)
        };
        var machine = new FishingStateMachine(options);
        var now = DateTimeOffset.UtcNow;

        var cast = machine.Step(new DetectionObservation(1, now), now);
        machine.Step(new DetectionObservation(2, now), now.AddSeconds(1));
        var fallback = machine.Step(new DetectionObservation(3, now), now.AddSeconds(3.1));

        var recovery = machine.Step(
            new DetectionObservation(4, now),
            now.AddSeconds(4.2));
        var recoveryWaiting = machine.Step(
            new DetectionObservation(5, now),
            now.AddSeconds(4.8));
        var resumed = machine.Step(
            new DetectionObservation(6, now),
            now.AddSeconds(5.3));
        var next = machine.Step(
            new DetectionObservation(7, now),
            now.AddSeconds(5.4));

        Assert.Equal(InputAction.Click, cast.Action);
        Assert.Equal(FishingPhase.Hooking, fallback.Phase);
        Assert.Equal(InputAction.Click, fallback.Action);
        Assert.Equal(FishingPhase.Recovery, recovery.Phase);
        Assert.Equal(InputAction.None, recovery.Action);
        Assert.Equal("bite fallback recovery", recovery.Reason);
        Assert.Equal(FishingPhase.Recovery, recoveryWaiting.Phase);
        Assert.Equal(InputAction.None, recoveryWaiting.Action);
        Assert.Equal(FishingPhase.WaitingForBite, resumed.Phase);
        Assert.Equal(InputAction.None, resumed.Action);
        Assert.Equal(FishingPhase.WaitingForBite, next.Phase);
        Assert.Equal(InputAction.None, next.Action);

        var secondFallback = machine.Step(
            new DetectionObservation(8, now),
            now.AddSeconds(7.4));
        var secondRecovery = machine.Step(
            new DetectionObservation(9, now),
            now.AddSeconds(8.5));
        var secondResumed = machine.Step(
            new DetectionObservation(10, now),
            now.AddSeconds(9.6));

        Assert.Equal(FishingPhase.Hooking, secondFallback.Phase);
        Assert.Equal(InputAction.Click, secondFallback.Action);
        Assert.Equal(FishingPhase.Recovery, secondRecovery.Phase);
        Assert.Equal(InputAction.None, secondRecovery.Action);
        Assert.Equal(FishingPhase.WaitingForBite, secondResumed.Phase);
        Assert.Equal(InputAction.None, secondResumed.Action);
    }

    [Fact]
    public void Minigame_end_waits_one_second_and_reels_once()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);
        machine.Step(new DetectionObservation(
            5,
            now.AddSeconds(1.4),
            MinigamePanel: Panel,
            PanelGeneration: 1), now.AddSeconds(1.4));

        StateDecision ended = default;
        for (var frame = 0; frame < StateMachineOptions.Default.UiLostFrames; frame++)
        {
            ended = machine.Step(
                new DetectionObservation(6 + frame, now.AddSeconds(1.5 + frame * 0.1)),
                now.AddSeconds(1.5 + frame * 0.1));
        }

        Assert.Equal(FishingPhase.Reeling, ended.Phase);
        Assert.Equal(InputAction.Release, ended.Action);

        var tooEarly = machine.Step(
            new DetectionObservation(20, now.AddSeconds(2.8)),
            now.AddSeconds(2.8));
        Assert.Equal(FishingPhase.Reeling, tooEarly.Phase);
        Assert.Equal(InputAction.None, tooEarly.Action);

        var reel = machine.Step(
            new DetectionObservation(21, now.AddSeconds(2.9)),
            now.AddSeconds(2.9));
        Assert.Equal(FishingPhase.Loot, reel.Phase);
        Assert.Equal(InputAction.Click, reel.Action);

        var duplicate = machine.Step(
            new DetectionObservation(22, now.AddSeconds(3.0)),
            now.AddSeconds(3.0));
        Assert.Equal(FishingPhase.Loot, duplicate.Phase);
        Assert.Equal(InputAction.None, duplicate.Action);
    }

    [Fact]
    public void Post_reel_delay_starts_after_reel_click_completes()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);

        StateDecision ended = default;
        for (var frame = 0; frame < StateMachineOptions.Default.UiLostFrames; frame++)
        {
            ended = machine.Step(
                new DetectionObservation(5 + frame, now.AddSeconds(1.4 + frame * 0.1)),
                now.AddSeconds(1.4 + frame * 0.1));
        }

        Assert.Equal(FishingPhase.Reeling, ended.Phase);
        var reelDecisionAt = now.AddSeconds(2.8);
        var reel = machine.Step(
            new DetectionObservation(20, reelDecisionAt),
            reelDecisionAt);
        var reelCompletedAt = now.AddSeconds(3.0);
        var nextCastNotBefore = machine.AcknowledgeInputCompleted(reel, reelCompletedAt);

        Assert.Equal(reelCompletedAt.AddSeconds(2), nextCastNotBefore);

        var tooEarly = machine.Step(
            new DetectionObservation(21, now.AddSeconds(4.9)),
            now.AddSeconds(4.9));
        Assert.Equal(FishingPhase.Loot, tooEarly.Phase);
        Assert.Equal(InputAction.None, tooEarly.Action);

        var ready = machine.Step(
            new DetectionObservation(22, now.AddSeconds(5.0)),
            now.AddSeconds(5.0));
        Assert.Equal(FishingPhase.Idle, ready.Phase);
        Assert.Equal(InputAction.None, ready.Action);

        var nextCast = machine.Step(
            new DetectionObservation(23, now.AddSeconds(5.1)),
            now.AddSeconds(5.1));
        Assert.Equal(FishingPhase.Casting, nextCast.Phase);
        Assert.Equal(InputAction.Click, nextCast.Action);
    }

    [Fact]
    public void First_fresh_frame_pulses_when_target_is_above_zone()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);

        var pulse = ControlFrame(
            machine, 5, now.AddSeconds(1.4), TimeSpan.FromSeconds(10), Zone, BoxAtCenter(37));

        Assert.Equal(InputAction.Pulse, pulse.Action);
        Assert.Equal(60, pulse.MinimumPulseDuration.TotalMilliseconds);
        Assert.NotNull(pulse.PredictedReleaseDelay);
        Assert.Equal(90, pulse.PredictedReleaseDelay!.Value.TotalMilliseconds);
        Assert.Contains("control=center_prediction", pulse.Diagnostic);
        Assert.Contains("velocity_up_px_s=0.00", pulse.Diagnostic);
        Assert.Contains("press acceleration unavailable; start measured pulse", pulse.Diagnostic);
    }

    [Fact]
    public void Fully_contained_target_keeps_input_released()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);

        var release = ControlFrame(
            machine, 5, now.AddSeconds(1.4), TimeSpan.FromSeconds(10), Zone, BoxAtCenter(50));

        Assert.Equal(InputAction.Release, release.Action);
        Assert.Null(release.PredictedReleaseDelay);
    }

    [Fact]
    public void Downward_velocity_that_will_cross_target_starts_pulse()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(machine, 5, now.AddSeconds(1.4), start, ZoneAt(36), BoxAtCenter(50));
        var pulse = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(44), BoxAtCenter(50));

        Assert.Equal(InputAction.Pulse, pulse.Action);
        Assert.Contains("velocity_up_px_s=-160.00", pulse.Diagnostic);
    }

    [Fact]
    public void Immediate_pulse_starts_when_waiting_one_control_interval_cannot_brake_in_time()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -4,
                PressAcceleration: 12));
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(
            machine, 5, now.AddSeconds(1.4), start,
            ZoneAt(46.5f), BoxAtCenter(50),
            controlTimestamp: start,
            currentMinigameInterval: TimeSpan.FromMilliseconds(40));
        var pulse = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(47), BoxAtCenter(50),
            controlTimestamp: start + TimeSpan.FromMilliseconds(50),
            currentMinigameInterval: TimeSpan.FromMilliseconds(40));

        Assert.Equal(InputAction.Pulse, pulse.Action);
        Assert.Contains("pulse candidate has greater two-sided margin", pulse.Diagnostic);
        Assert.Contains("decision_interval_p95_ms=50.0", pulse.Diagnostic);
        Assert.Contains("wait_brake_min_up=-58.01", pulse.Diagnostic);
        Assert.Contains("pulse_violation_px=0.00", pulse.Diagnostic);
    }

    [Fact]
    public void Contained_target_does_not_start_a_new_pulse_near_the_upper_edge()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -4,
                PressAcceleration: 12));
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(
            machine, 5, now.AddSeconds(1.4), start,
            ZoneAt(33.5f), BoxAtCenter(50),
            controlTimestamp: start);
        var release = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(33), BoxAtCenter(50),
            controlTimestamp: start + TimeSpan.FromMilliseconds(50));

        Assert.Equal(InputAction.Release, release.Action);
        Assert.Contains("target contained; keep released", release.Diagnostic);
        Assert.Contains("pulse_min_ms=0", release.Diagnostic);
    }

    [Fact]
    public void Upper_boundary_constraint_keeps_a_safe_release_plan_unchanged()
    {
        var planned = TimeSpan.FromMilliseconds(100);

        var constrained = MinigameController.ConstrainReleaseDelayForUpperBoundary(
            position: 0,
            velocity: 0,
            upperBoundary: 3,
            pressAcceleration: 10,
            releaseAcceleration: -10,
            earliestReleaseDelay: TimeSpan.FromMilliseconds(60),
            plannedReleaseDelay: planned,
            referenceHeight: 20);

        Assert.Equal(planned, constrained);
    }

    [Fact]
    public void Upper_boundary_constraint_truncates_an_unsafe_release_plan()
    {
        var constrained = MinigameController.ConstrainReleaseDelayForUpperBoundary(
            position: 0,
            velocity: 0,
            upperBoundary: 1,
            pressAcceleration: 10,
            releaseAcceleration: -10,
            earliestReleaseDelay: TimeSpan.FromMilliseconds(60),
            plannedReleaseDelay: TimeSpan.FromMilliseconds(100),
            referenceHeight: 20);

        Assert.NotNull(constrained);
        Assert.InRange(constrained.Value.TotalMilliseconds, 70, 71);
    }

    [Fact]
    public void Upper_boundary_constraint_rejects_when_the_earliest_release_is_too_late()
    {
        var constrained = MinigameController.ConstrainReleaseDelayForUpperBoundary(
            position: 0,
            velocity: 0,
            upperBoundary: 0.5,
            pressAcceleration: 10,
            releaseAcceleration: -10,
            earliestReleaseDelay: TimeSpan.FromMilliseconds(60),
            plannedReleaseDelay: TimeSpan.FromMilliseconds(100),
            referenceHeight: 20);

        Assert.Null(constrained);
    }

    [Fact]
    public void Pressed_contained_target_requests_release_before_overshoot()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -4,
                PressAcceleration: 12));

        var decision = ControlFrame(
            machine,
            5,
            now.AddSeconds(1.4),
            TimeSpan.FromSeconds(10),
            ZoneAt(32.4f),
            BoxAtCenter(50),
            inputState: MinigameInputState.Pressed,
            remainingMinimumHold: TimeSpan.FromMilliseconds(30));

        Assert.Equal(InputAction.Release, decision.Action);
        Assert.Null(decision.PredictedRepressDelay);
        Assert.Contains("target contained; release pulse", decision.Diagnostic);
    }

    [Fact]
    public void Fresh_coordinates_recompute_the_upper_boundary_limit()
    {
        var firstLimit = MinigameController.ConstrainReleaseDelayForUpperBoundary(
            position: 0,
            velocity: 0,
            upperBoundary: 1,
            pressAcceleration: 10,
            releaseAcceleration: -10,
            earliestReleaseDelay: TimeSpan.FromMilliseconds(60),
            plannedReleaseDelay: TimeSpan.FromMilliseconds(100),
            referenceHeight: 20);
        var updatedLimit = MinigameController.ConstrainReleaseDelayForUpperBoundary(
            position: 0.3,
            velocity: 0,
            upperBoundary: 1,
            pressAcceleration: 10,
            releaseAcceleration: -10,
            earliestReleaseDelay: TimeSpan.FromMilliseconds(60),
            plannedReleaseDelay: TimeSpan.FromMilliseconds(100),
            referenceHeight: 20);

        Assert.NotNull(firstLimit);
        Assert.Null(updatedLimit);
    }

    [Fact]
    public void Braking_prediction_uses_actual_control_decision_interval_not_capture_interval()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -4,
                PressAcceleration: 12));
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(
            machine, 5, now.AddSeconds(1.4), start,
            ZoneAt(46.5f), BoxAtCenter(50),
            controlTimestamp: start + TimeSpan.FromMilliseconds(20));
        var decision = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(47), BoxAtCenter(50),
            controlTimestamp: start + TimeSpan.FromMilliseconds(120));

        Assert.Contains("decision_interval_p95_ms=100.0", decision.Diagnostic);
    }

    [Fact]
    public void Contained_target_remains_released_with_or_without_release_acceleration()
    {
        var now = DateTimeOffset.UtcNow;
        var timestamp = TimeSpan.FromSeconds(10);
        var linearMachine = EnterMinigame(now);
        var dynamicMachine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(ReleaseAcceleration: -10));
        var nearUpperEdge = ZoneAt(47.9f);

        var linear = ControlFrame(
            linearMachine, 5, now.AddSeconds(1.4), timestamp, nearUpperEdge, BoxAtCenter(50));
        var dynamic = ControlFrame(
            dynamicMachine, 5, now.AddSeconds(1.4), timestamp, nearUpperEdge, BoxAtCenter(50));

        Assert.Equal(InputAction.Release, linear.Action);
        Assert.Equal(InputAction.Release, dynamic.Action);
        Assert.Contains("target contained; keep released", dynamic.Diagnostic);
        Assert.Contains("release_accel_up_h_s2=-10.000", dynamic.Diagnostic);
    }

    [Fact]
    public void Upward_moving_contained_zone_releases_an_active_pulse()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -4,
                PressAcceleration: 12));
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(
            machine, 5, now.AddSeconds(1.4), start,
            ZoneAt(45), BoxAtCenter(50),
            inputState: MinigameInputState.Pressed);
        var release = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(43), BoxAtCenter(50),
            inputState: MinigameInputState.Pressed);

        Assert.Equal(InputAction.Release, release.Action);
        Assert.Null(release.PredictedReleaseDelay);
        Assert.Contains("target contained; release pulse", release.Diagnostic);
        Assert.Contains("velocity_up_px_s=40.00", release.Diagnostic);
    }

    [Fact]
    public void Slow_downward_motion_inside_the_zone_stays_released()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -4,
                PressAcceleration: 12));
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(
            machine, 5, now.AddSeconds(1.4), start,
            ZoneAt(42), BoxAtCenter(50));
        var release = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(42.25f), BoxAtCenter(50));

        Assert.Equal(InputAction.Release, release.Action);
        Assert.Contains("target contained; keep released", release.Diagnostic);
        Assert.Contains("velocity_up_px_s=-5.00", release.Diagnostic);
    }

    [Fact]
    public void Outside_target_is_recovered_near_the_containment_edge_not_the_midpoint()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -4,
                PressAcceleration: 12));

        var pulse = ControlFrame(
            machine, 5, now.AddSeconds(1.4), TimeSpan.FromSeconds(10),
            Zone, BoxAtCenter(37));

        Assert.Equal(InputAction.Pulse, pulse.Action);
        Assert.NotNull(pulse.PredictedReleaseDelay);
        Assert.InRange(pulse.PredictedReleaseDelay.Value.TotalMilliseconds, 100, 120);
    }

    [Fact]
    public void Upward_stopping_position_releases_pressed_pulse_before_current_position_arrives()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -5,
                PressAcceleration: 20));
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(
            machine, 5, now.AddSeconds(1.4), start,
            ZoneAt(60), BoxAtCenter(50),
            inputState: MinigameInputState.Pressed);
        var release = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(52), BoxAtCenter(50),
            inputState: MinigameInputState.Pressed);

        Assert.Equal(InputAction.Release, release.Action);
        Assert.Contains("upper boundary requires earliest release", release.Diagnostic);
        Assert.Contains("press_accel_up_h_s2=20.000", release.Diagnostic);
    }

    [Fact]
    public void Three_consistent_release_samples_replace_the_initial_value()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(ReleaseAcceleration: -1));
        var start = TimeSpan.FromSeconds(10);
        var tops = new[] { 40f, 40.1f, 40.4f, 40.9f, 41.6f };

        for (var index = 0; index < tops.Length; index++)
        {
            _ = ControlFrame(
                machine,
                5 + index,
                now.AddMilliseconds(1400 + index * 50),
                start + TimeSpan.FromMilliseconds(index * 50),
                ZoneAt(tops[index]),
                BoxAtCenter(50));
        }

        Assert.NotNull(machine.MinigameDynamics.ReleaseAcceleration);
        Assert.InRange(
            machine.MinigameDynamics.ReleaseAcceleration!.Value,
            -4.01,
            -3.99);
    }

    [Fact]
    public void Large_real_displacements_are_not_discarded_by_an_arbitrary_height_threshold()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(ReleaseAcceleration: -1));
        var start = TimeSpan.FromSeconds(10);
        var tops = new[] { 0f, 16f, 34f, 54f, 76f };

        for (var index = 0; index < tops.Length; index++)
        {
            _ = ControlFrame(
                machine,
                5 + index,
                now.AddMilliseconds(1400 + index * 50),
                start + TimeSpan.FromMilliseconds(index * 50),
                ZoneAt(tops[index]),
                BoxAtCenter(-100));
        }

        Assert.NotNull(machine.MinigameDynamics.ReleaseAcceleration);
        Assert.InRange(
            machine.MinigameDynamics.ReleaseAcceleration!.Value,
            -40.01,
            -39.99);
    }

    [Fact]
    public void Three_continuous_pressed_samples_measure_press_acceleration()
    {
        const double pressAcceleration = 12;
        const double referenceHeight = 20;
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(ReleaseAcceleration: -4));
        var timestamp = TimeSpan.FromSeconds(10);
        const double initialCenterUp = -60;

        for (var index = 0; index < 5; index++)
        {
            var elapsedSeconds = index * 0.05;
            var centerUp = initialCenterUp
                + 0.5 * pressAcceleration * elapsedSeconds * elapsedSeconds * referenceHeight;
            _ = ControlFrame(
                machine,
                5 + index,
                now.AddMilliseconds(1400 + index * 50),
                timestamp + TimeSpan.FromMilliseconds(index * 50),
                ZoneAtCenterUp(centerUp, referenceHeight),
                BoxAtCenter(-100),
                inputState: MinigameInputState.Pressed);
        }

        Assert.NotNull(machine.MinigameDynamics.PressAcceleration);
        Assert.InRange(
            machine.MinigameDynamics.PressAcceleration!.Value,
            11.99,
            12.01);
    }

    [Fact]
    public void Upward_velocity_that_will_reach_target_does_not_pulse()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(machine, 5, now.AddSeconds(1.4), start, ZoneAt(44), BoxAtCenter(37));
        var release = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(39), BoxAtCenter(37));

        Assert.Equal(InputAction.Release, release.Action);
        Assert.Contains("velocity_up_px_s=100.00", release.Diagnostic);
    }

    [Fact]
    public void Upward_velocity_that_will_cross_lower_edge_remains_released()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(machine, 5, now.AddSeconds(1.4), start, ZoneAt(40), BoxAtCenter(50));
        var release = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(36), BoxAtCenter(50));

        Assert.Equal(InputAction.Release, release.Action);
        Assert.Contains("keep released", release.Diagnostic);
    }

    [Fact]
    public void Pressed_pulse_continues_while_predicted_zone_is_still_too_low()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);

        var decision = ControlFrame(
            machine, 5, now.AddSeconds(1.4), TimeSpan.FromSeconds(10), Zone, BoxAtCenter(37),
            inputState: MinigameInputState.Pressed,
            remainingMinimumHold: TimeSpan.FromMilliseconds(30));

        Assert.Equal(InputAction.None, decision.Action);
        Assert.Contains("continue pulse", decision.Diagnostic);
    }

    [Fact]
    public void Pressed_and_still_falling_keeps_pressing_when_release_would_cross_lower_bound()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -4,
                PressAcceleration: 12));
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(
            machine, 5, now.AddSeconds(1.4), start,
            ZoneAt(40), BoxAtCenter(50),
            inputState: MinigameInputState.Pressed);
        var decision = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(45), BoxAtCenter(50),
            inputState: MinigameInputState.Pressed);

        Assert.Equal(InputAction.None, decision.Action);
        Assert.NotNull(decision.PredictedReleaseDelay);
        Assert.Contains("continue pulse has greater two-sided margin", decision.Diagnostic);
        Assert.Contains("velocity_up_px_s=-100.00", decision.Diagnostic);
    }

    [Fact]
    public void Pressed_pulse_requests_release_once_rise_is_no_longer_needed()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);

        var decision = ControlFrame(
            machine, 5, now.AddSeconds(1.4), TimeSpan.FromSeconds(10), ZoneAt(34), BoxAtCenter(37),
            inputState: MinigameInputState.Pressed,
            remainingMinimumHold: TimeSpan.FromMilliseconds(20));

        Assert.Equal(InputAction.Release, decision.Action);
        Assert.Contains("release while press acceleration is unavailable", decision.Diagnostic);
    }

    [Fact]
    public void Cooldown_never_starts_a_new_pulse()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);

        var cooldown = ControlFrame(
            machine, 5, now.AddSeconds(1.4), TimeSpan.FromSeconds(10), Zone, BoxAtCenter(37),
            inputState: MinigameInputState.Cooldown);

        Assert.Equal(InputAction.None, cooldown.Action);
        Assert.Contains("pulse cooldown", cooldown.Diagnostic);
    }

    [Fact]
    public void Duplicate_cached_observation_does_not_repeat_an_action()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);
        var timestamp = TimeSpan.FromSeconds(10);

        var pulse = ControlFrame(machine, 5, now.AddSeconds(1.4), timestamp, Zone, BoxAtCenter(37));
        var duplicate = ControlFrame(machine, 5, now.AddSeconds(1.5), timestamp, Zone, BoxAtCenter(37));

        Assert.Equal(InputAction.Pulse, pulse.Action);
        Assert.Equal(InputAction.None, duplicate.Action);
        Assert.Contains("fresh control frame", duplicate.Reason);
    }

    [Theory]
    [InlineData(151)]
    [InlineData(-1)]
    public void Invalid_control_frame_age_only_releases(int ageMilliseconds)
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);
        var captured = TimeSpan.FromSeconds(10);

        var decision = ControlFrame(
            machine, 5, now.AddSeconds(1.4), captured, Zone, BoxAtCenter(37),
            controlTimestamp: captured + TimeSpan.FromMilliseconds(ageMilliseconds));

        Assert.Equal(InputAction.Release, decision.Action);
        Assert.Contains("control frame stale", decision.Reason);
    }

    [Fact]
    public void Consecutive_missing_components_release_and_reset_motion_history()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(machine, 5, now.AddSeconds(1.4), start, ZoneAt(35), BoxAtCenter(50));
        var missingTimestamp = start + TimeSpan.FromMilliseconds(50);
        var missing = machine.Step(
            new DetectionObservation(
                6, now.AddSeconds(1.45), MinigamePanel: Panel, PanelGeneration: 1,
                CapturedTimestamp: missingTimestamp),
            now.AddSeconds(1.45),
            MinigameInputState.Released,
            missingTimestamp,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(50));
        var secondMissingTimestamp = start + TimeSpan.FromMilliseconds(100);
        var secondMissing = machine.Step(
            new DetectionObservation(
                7, now.AddSeconds(1.5), MinigamePanel: Panel, PanelGeneration: 1,
                CapturedTimestamp: secondMissingTimestamp),
            now.AddSeconds(1.5),
            MinigameInputState.Released,
            secondMissingTimestamp,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(50));
        var restored = ControlFrame(
            machine, 8, now.AddSeconds(1.55), start + TimeSpan.FromMilliseconds(150),
            Zone, BoxAtCenter(37));

        Assert.Equal(InputAction.None, missing.Action);
        Assert.Equal(InputAction.Release, secondMissing.Action);
        Assert.Equal(InputAction.Pulse, restored.Action);
        Assert.Contains("velocity_up_px_s=0.00", restored.Diagnostic);
    }

    [Fact]
    public void Panel_relocation_releases_and_resets_motion_history()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);
        var start = TimeSpan.FromSeconds(10);
        var pulse = ControlFrame(machine, 5, now.AddSeconds(1.4), start, Zone, BoxAtCenter(37));

        var relocated = ControlFrame(
            machine, 6, now.AddSeconds(1.5), start + TimeSpan.FromMilliseconds(50),
            Zone, BoxAtCenter(37), panelGeneration: 2,
            panel: new BoundingBox(20, 0, 120, 100));
        var firstAfterRelocation = ControlFrame(
            machine, 7, now.AddSeconds(1.6), start + TimeSpan.FromMilliseconds(100),
            Zone, BoxAtCenter(37), panelGeneration: 2,
            panel: new BoundingBox(20, 0, 120, 100));

        Assert.Equal(InputAction.Pulse, pulse.Action);
        Assert.Equal(InputAction.Release, relocated.Action);
        Assert.Equal(InputAction.Pulse, firstAfterRelocation.Action);
        Assert.Contains("velocity_up_px_s=0.00", firstAfterRelocation.Diagnostic);
    }

    [Fact]
    public void Equal_height_target_is_valid_but_taller_target_releases()
    {
        var now = DateTimeOffset.UtcNow;
        var equalMachine = EnterMinigame(now);
        var tallerMachine = EnterMinigame(now);
        var timestamp = TimeSpan.FromSeconds(10);

        var equal = ControlFrame(
            equalMachine, 5, now.AddSeconds(1.4), timestamp, Zone,
            new BoundingBox(10, 40, 30, 60));
        var taller = ControlFrame(
            tallerMachine, 5, now.AddSeconds(1.4), timestamp, Zone,
            new BoundingBox(10, 35, 30, 65));

        Assert.Equal(InputAction.Release, equal.Action);
        Assert.DoesNotContain("geometry impossible", equal.Diagnostic);
        Assert.Equal(InputAction.Release, taller.Action);
        Assert.Contains("geometry impossible", taller.Diagnostic);
    }

    [Fact]
    public void Pulse_plan_is_predicted_and_not_randomized()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);
        var start = TimeSpan.FromSeconds(10);

        for (var index = 0; index < 20; index++)
        {
            var decision = ControlFrame(
                machine, 5 + index, now.AddMilliseconds(1400 + index * 40),
                start + TimeSpan.FromMilliseconds(index * 40), Zone, BoxAtCenter(37));

            Assert.Equal(InputAction.Pulse, decision.Action);
            Assert.Equal(90, decision.PredictedReleaseDelay!.Value.TotalMilliseconds);
        }
    }

    [Fact]
    public void Target_rising_while_zone_falls_pulses_before_relative_lower_boundary_crossing()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -4,
                PressAcceleration: 12));
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(
            machine, 5, now.AddSeconds(1.4), start,
            ZoneAt(40), BoxAtCenter(60));
        var decision = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(44), BoxAtCenter(54),
            currentMinigameInterval: TimeSpan.FromMilliseconds(50));

        Assert.Equal(InputAction.Pulse, decision.Action);
        Assert.Contains("target_velocity_up_px_s=120.00", decision.Diagnostic);
        Assert.Contains("target_prediction_velocity_up_px_s=90.00", decision.Diagnostic);
        Assert.Contains("relative_velocity_up_px_s=-170.00", decision.Diagnostic);
    }

    [Fact]
    public void Target_falling_while_zone_rises_releases_before_relative_upper_boundary_crossing()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -4,
                PressAcceleration: 12));
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(
            machine, 5, now.AddSeconds(1.4), start,
            ZoneAt(40), BoxAtCenter(42));
        var decision = ControlFrame(
            machine, 6, now.AddSeconds(1.45), start + TimeSpan.FromMilliseconds(50),
            ZoneAt(36), BoxAtCenter(48),
            currentMinigameInterval: TimeSpan.FromMilliseconds(50));

        Assert.Equal(InputAction.Release, decision.Action);
        Assert.Contains("target_velocity_up_px_s=-120.00", decision.Diagnostic);
        Assert.Contains("target_prediction_velocity_up_px_s=-90.00", decision.Diagnostic);
        Assert.Contains("relative_velocity_up_px_s=170.00", decision.Diagnostic);
    }

    [Fact]
    public void Frame_age_compensation_uses_actual_input_transition_timeline()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(
            now,
            new MinigameDynamicsParameters(
                ReleaseAcceleration: -4,
                PressAcceleration: 12));
        var captured = TimeSpan.FromSeconds(10);
        var timeline = new MinigameInputTimeline(
            MinigameInputState.Released,
            [new(captured + TimeSpan.FromMilliseconds(50), MinigameInputState.Pressed)]);

        var decision = ControlFrame(
            machine,
            5,
            now.AddSeconds(1.4),
            captured,
            ZoneAt(40),
            BoxAtCenter(50),
            inputState: MinigameInputState.Pressed,
            controlTimestamp: captured + TimeSpan.FromMilliseconds(100),
            inputTimeline: timeline);

        Assert.Contains("current_relative_up=0.00", decision.Diagnostic);
        Assert.Contains("relative_velocity_up_h_s=0.400", decision.Diagnostic);
        Assert.Contains("zone_current_up=-50.00", decision.Diagnostic);
        Assert.Contains("input_state_at_capture=Released input_transition_count=1", decision.Diagnostic);
    }

    [Fact]
    public void Target_is_projected_through_frame_age_and_next_feedback_interval()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(
            machine,
            5,
            now.AddSeconds(1.4),
            start,
            ZoneAt(40),
            BoxAtCenter(50),
            controlTimestamp: start);
        var decision = ControlFrame(
            machine,
            6,
            now.AddSeconds(1.45),
            start + TimeSpan.FromMilliseconds(50),
            ZoneAt(40),
            BoxAtCenter(45),
            controlTimestamp: start + TimeSpan.FromMilliseconds(100),
            currentMinigameInterval: TimeSpan.FromMilliseconds(50));

        Assert.Contains("target_velocity_up_px_s=100.00", decision.Diagnostic);
        Assert.Contains("frame_age_ms=50.0 decision_interval_p95_ms=100.0", decision.Diagnostic);
        Assert.Contains("target_prediction_velocity_up_px_s=50.00", decision.Diagnostic);
        Assert.Contains("target_prediction_weight=0.500", decision.Diagnostic);
        Assert.Contains("target_current_up=-40.00 target_feedback_up=-35.00", decision.Diagnostic);
    }

    [Fact]
    public void Random_target_reversal_uses_the_latest_two_frames()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = EnterMinigame(now);
        var start = TimeSpan.FromSeconds(10);

        _ = ControlFrame(machine, 5, now.AddSeconds(1.4), start,
            ZoneAt(40), BoxAtCenter(50), controlTimestamp: start);
        _ = ControlFrame(machine, 6, now.AddSeconds(1.45),
            start + TimeSpan.FromMilliseconds(50), ZoneAt(40), BoxAtCenter(45),
            controlTimestamp: start + TimeSpan.FromMilliseconds(50));
        var reversed = ControlFrame(machine, 7, now.AddSeconds(1.5),
            start + TimeSpan.FromMilliseconds(100), ZoneAt(40), BoxAtCenter(47),
            controlTimestamp: start + TimeSpan.FromMilliseconds(100));

        Assert.Contains("target_velocity_up_px_s=-40.00", reversed.Diagnostic);
        Assert.DoesNotContain("target_velocity_up_px_s=30.00", reversed.Diagnostic);
    }

    [Theory]
    [InlineData(60, 60, 0.75)]
    [InlineData(60, 90, 2.0 / 3.0)]
    [InlineData(60, 120, 0.50)]
    [InlineData(60, 150, 0.50)]
    public void Target_prediction_weight_tracks_the_real_feedback_period(
        int targetIntervalMs,
        int feedbackMs,
        double expected)
    {
        var actual = MinigameController.TargetPredictionWeight(
            TimeSpan.FromMilliseconds(targetIntervalMs),
            TimeSpan.FromMilliseconds(feedbackMs));

        Assert.Equal(expected, actual, precision: 6);
    }

    private static FishingStateMachine EnterMinigame(
        DateTimeOffset now,
        MinigameDynamicsParameters? initialDynamics = null)
    {
        var options = StateMachineOptions.Default with
        {
            BiteIndicatorConfirmFrames = 1,
            UiConfirmFrames = 1
        };
        var machine = new FishingStateMachine(options, initialDynamics);
        machine.Step(new DetectionObservation(1, now), now);
        machine.Step(new DetectionObservation(2, now.AddSeconds(1)), now.AddSeconds(1));
        machine.Step(new DetectionObservation(
            3,
            now.AddSeconds(1.1),
            BiteIndicator: new BoundingBox(0, 0, 10, 10)), now.AddSeconds(1.1));
        var entered = machine.Step(new DetectionObservation(
            4,
            now.AddSeconds(1.3),
            MinigamePanel: Panel), now.AddSeconds(1.3));
        Assert.Equal(FishingPhase.Minigame, entered.Phase);
        return machine;
    }

    private static StateDecision ControlFrame(
        FishingStateMachine machine,
        long frameNumber,
        DateTimeOffset capturedAt,
        TimeSpan capturedTimestamp,
        BoundingBox zone,
        BoundingBox target,
        long panelGeneration = 1,
        BoundingBox? panel = null,
        MinigameInputState inputState = MinigameInputState.Released,
        TimeSpan remainingMinimumHold = default,
        TimeSpan? controlTimestamp = null,
        TimeSpan? currentMinigameInterval = null,
        MinigameInputTimeline? inputTimeline = null) => machine.Step(
            new DetectionObservation(
                frameNumber,
                capturedAt,
                MinigamePanel: panel ?? Panel,
                CatchZone: zone,
                MovingTarget: target,
                PanelGeneration: panelGeneration,
                CapturedTimestamp: capturedTimestamp),
            capturedAt,
            inputState,
            controlTimestamp ?? capturedTimestamp,
            remainingMinimumHold,
            currentMinigameInterval ?? TimeSpan.FromMilliseconds(40),
            inputTimeline);

    private static BoundingBox BoxAtCenter(float centerY) =>
        new(10, centerY - 2, 30, centerY + 2);

    private static BoundingBox ZoneAt(float top) =>
        new(10, top, 30, top + 20);

    private static BoundingBox ZoneAtCenterUp(double centerUp, double height)
    {
        var screenCenter = -centerUp;
        var top = screenCenter - height / 2.0;
        return new(10, (float)top, 30, (float)(top + height));
    }
}
