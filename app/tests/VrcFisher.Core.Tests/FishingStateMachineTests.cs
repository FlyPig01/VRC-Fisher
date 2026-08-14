using VrcFisher.Core;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class FishingStateMachineTests
{
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

        var onePrompt = machine.Step(new DetectionObservation(3, start, BiteIndicator: new BoundingBox(1, 1, 2, 2)), start.AddSeconds(1.1));
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
    public void Stop_releases_held_mouse_and_is_idempotent()
    {
        var machine = new FishingStateMachine(StateMachineOptions.Default);
        var now = DateTimeOffset.UtcNow;
        machine.Step(new DetectionObservation(1, now), now);
        machine.Step(new DetectionObservation(2, now), now.AddSeconds(1));

        var stop = machine.Stop(now.AddSeconds(2));
        Assert.Equal(FishingPhase.Stopped, stop.Phase);
        Assert.Equal(InputAction.None, stop.Action);

        var second = machine.Stop(now.AddSeconds(3));
        Assert.Equal(InputAction.None, second.Action);
    }

    [Fact]
    public void Reset_after_stop_starts_a_new_cycle()
    {
        var machine = new FishingStateMachine(StateMachineOptions.Default);
        var now = DateTimeOffset.UtcNow;
        machine.Stop(now);

        machine.Reset(now.AddSeconds(1));
        var decision = machine.Step(new DetectionObservation(1, now.AddSeconds(1)), now.AddSeconds(1));

        Assert.Equal(FishingPhase.Casting, decision.Phase);
        Assert.Equal(InputAction.Click, decision.Action);
    }

    [Fact]
    public void Missing_target_does_not_press_mouse()
    {
        var options = StateMachineOptions.Default with { BiteIndicatorConfirmFrames = 1, UiConfirmFrames = 1 };
        var machine = new FishingStateMachine(options);
        var now = DateTimeOffset.UtcNow;
        machine.Step(new DetectionObservation(1, now), now);
        machine.Step(new DetectionObservation(2, now), now.AddSeconds(1));
        machine.Step(new DetectionObservation(3, now, BiteIndicator: new BoundingBox(0, 0, 10, 10)), now.AddSeconds(1.1));
        machine.Step(new DetectionObservation(4, now, MinigamePanel: new BoundingBox(0, 0, 100, 100)), now.AddSeconds(1.3));

        var decision = machine.Step(new DetectionObservation(5, now, MinigamePanel: new BoundingBox(0, 0, 100, 100)), now.AddSeconds(1.4));
        Assert.Equal(FishingPhase.Minigame, decision.Phase);
        Assert.Equal(InputAction.None, decision.Action);
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
    public void Minigame_ends_after_panel_disappears_and_reels_once()
    {
        var options = StateMachineOptions.Default with
        {
            BiteIndicatorConfirmFrames = 1,
            UiConfirmFrames = 1
        };
        var machine = new FishingStateMachine(options);
        var now = DateTimeOffset.UtcNow;
        machine.Step(new DetectionObservation(1, now), now);
        machine.Step(new DetectionObservation(2, now), now.AddSeconds(1));
        machine.Step(new DetectionObservation(3, now, BiteIndicator: new BoundingBox(0, 0, 10, 10)), now.AddSeconds(1.1));
        machine.Step(new DetectionObservation(4, now, MinigamePanel: new BoundingBox(0, 0, 100, 100)), now.AddSeconds(1.3));

        machine.Step(new DetectionObservation(
            5,
            now,
            MinigamePanel: new BoundingBox(0, 0, 100, 100)), now.AddSeconds(1.4));

        StateDecision ended = default;
        for (var frame = 0; frame < StateMachineOptions.Default.UiLostFrames; frame++)
        {
            ended = machine.Step(new DetectionObservation(6 + frame, now), now.AddSeconds(1.5 + frame * 0.1));
        }

        Assert.Equal(FishingPhase.Reeling, ended.Phase);
        var reel = machine.Step(new DetectionObservation(20, now), now.AddSeconds(2.1));
        Assert.Equal(FishingPhase.Loot, reel.Phase);
        Assert.Equal(InputAction.Click, reel.Action);
    }

    [Fact]
    public void Minigame_moves_the_catch_zone_toward_the_moving_target()
    {
        var options = StateMachineOptions.Default with { BiteIndicatorConfirmFrames = 1, UiConfirmFrames = 1 };
        var machine = new FishingStateMachine(options);
        var now = DateTimeOffset.UtcNow;
        var panel = new BoundingBox(0, 0, 100, 100);
        var zone = new BoundingBox(10, 40, 30, 60);
        var target = new BoundingBox(10, 10, 30, 20);
        machine.Step(new DetectionObservation(1, now), now);
        machine.Step(new DetectionObservation(2, now), now.AddSeconds(1));
        machine.Step(new DetectionObservation(3, now, BiteIndicator: zone), now.AddSeconds(1.1));
        machine.Step(new DetectionObservation(4, now, MinigamePanel: panel), now.AddSeconds(1.3));

        var press = machine.Step(new DetectionObservation(
            5,
            now,
            MinigamePanel: panel,
            CatchZone: zone,
            MovingTarget: target,
            MovingTargetYNorm: 0f,
            CatchZoneTopNorm: 0f,
            CatchZoneBottomNorm: 1f), now.AddSeconds(1.4));
        var release = machine.Step(new DetectionObservation(
            6,
            now,
            MinigamePanel: panel,
            CatchZone: zone,
            MovingTarget: target,
            MovingTargetYNorm: 1f,
            CatchZoneTopNorm: 0f,
            CatchZoneBottomNorm: 1f), now.AddSeconds(1.5));

        Assert.Equal(InputAction.Press, press.Action);
        Assert.Equal(InputAction.Release, release.Action);
    }

    [Fact]
    public void Minigame_releases_input_when_the_catch_zone_is_missing()
    {
        var options = StateMachineOptions.Default with { BiteIndicatorConfirmFrames = 1, UiConfirmFrames = 1 };
        var machine = new FishingStateMachine(options);
        var now = DateTimeOffset.UtcNow;
        var panel = new BoundingBox(0, 0, 100, 100);
        var zone = new BoundingBox(10, 40, 30, 60);
        var target = new BoundingBox(10, 10, 30, 20);
        machine.Step(new DetectionObservation(1, now), now);
        machine.Step(new DetectionObservation(2, now), now.AddSeconds(1));
        machine.Step(new DetectionObservation(3, now, BiteIndicator: zone), now.AddSeconds(1.1));
        machine.Step(new DetectionObservation(4, now, MinigamePanel: panel), now.AddSeconds(1.3));
        machine.Step(new DetectionObservation(
            5,
            now,
            MinigamePanel: panel,
            CatchZone: zone,
            MovingTarget: target), now.AddSeconds(1.4));

        var missingZone = machine.Step(new DetectionObservation(
            6,
            now,
            MinigamePanel: panel,
            MovingTarget: target), now.AddSeconds(1.5));

        Assert.Equal(InputAction.Release, missingZone.Action);
    }
}
