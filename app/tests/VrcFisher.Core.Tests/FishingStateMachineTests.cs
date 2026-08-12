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

        var onePrompt = machine.Step(new DetectionObservation(3, start, Prompt: new BoundingBox(1, 1, 2, 2)), start.AddSeconds(1.1));
        Assert.Equal(FishingPhase.WaitingForBite, onePrompt.Phase);
        Assert.Equal(InputAction.None, onePrompt.Action);
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
    public void Missing_target_does_not_press_mouse()
    {
        var options = StateMachineOptions.Default with { PromptConfirmFrames = 1, UiConfirmFrames = 1 };
        var machine = new FishingStateMachine(options);
        var now = DateTimeOffset.UtcNow;
        machine.Step(new DetectionObservation(1, now), now);
        machine.Step(new DetectionObservation(2, now), now.AddSeconds(1));
        machine.Step(new DetectionObservation(3, now, Prompt: new BoundingBox(0, 0, 10, 10)), now.AddSeconds(1.1));
        machine.Step(new DetectionObservation(4, now, FishingUi: new BoundingBox(0, 0, 100, 100)), now.AddSeconds(1.3));

        var decision = machine.Step(new DetectionObservation(5, now, FishingUi: new BoundingBox(0, 0, 100, 100)), now.AddSeconds(1.4));
        Assert.Equal(FishingPhase.Minigame, decision.Phase);
        Assert.Equal(InputAction.None, decision.Action);
    }
}
