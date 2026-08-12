namespace VrcFisher.Core;

public sealed class FishingStateMachine(StateMachineOptions options) : IStateMachine
{
    private readonly Evidence _evidence = new();
    private FishingPhase _phase = FishingPhase.Idle;
    private DateTimeOffset _enteredAt;
    private bool _leftHeld;
    private int _cycle;

    public FishingPhase Phase => _phase;

    public StateDecision Step(DetectionObservation observation, DateTimeOffset now)
    {
        _evidence.Update(observation);

        if (_evidence.Failure >= options.FailureConfirmFrames &&
            _phase is not (FishingPhase.Idle or FishingPhase.Casting or FishingPhase.Recovery or FishingPhase.Stopped))
        {
            return Recover(now, "failure detected");
        }

        if (_phase == FishingPhase.Idle)
        {
            _cycle++;
            Transition(FishingPhase.Casting, now);
            return Decision(InputAction.Click, "cast");
        }

        var elapsed = now - _enteredAt;
        switch (_phase)
        {
            case FishingPhase.Casting when elapsed >= options.CastSettle:
                Transition(FishingPhase.WaitingForBite, now);
                break;
            case FishingPhase.WaitingForBite:
                if (_evidence.Prompt >= options.PromptConfirmFrames)
                {
                    Transition(FishingPhase.Hooking, now);
                    return Decision(InputAction.Click, "bite confirmed");
                }
                if (elapsed >= options.BiteTimeout) return Recover(now, "bite timeout");
                break;
            case FishingPhase.Hooking:
                if (elapsed >= options.HookToUiMinimum && _evidence.Ui >= options.UiConfirmFrames)
                {
                    Transition(FishingPhase.Minigame, now);
                    return Decision(InputAction.None, "minigame confirmed");
                }
                if (elapsed >= options.PromptToUiTimeout) return Recover(now, "minigame did not start");
                break;
            case FishingPhase.Minigame:
                if (_evidence.UiLost >= options.UiLostFrames)
                {
                    var release = ReleaseIfHeld();
                    Transition(FishingPhase.Reeling, now);
                    return Decision(release, "minigame ended");
                }
                if (elapsed >= options.MinigameTimeout) return Recover(now, "minigame timeout");
                return Decision(MinigameAction(observation), "follow target");
            case FishingPhase.Reeling:
                Transition(FishingPhase.Loot, now);
                return Decision(InputAction.Click, "reel and collect");
            case FishingPhase.Loot:
                if (elapsed >= options.CycleDelay)
                {
                    Transition(FishingPhase.Idle, now);
                    return Decision(InputAction.None, "next cycle");
                }
                if (elapsed >= options.LootTimeout)
                {
                    Transition(FishingPhase.Idle, now);
                    return Decision(InputAction.None, "loot timeout");
                }
                break;
            case FishingPhase.Recovery when elapsed >= options.RecoveryDelay:
                Transition(FishingPhase.Idle, now);
                return Decision(InputAction.None, "recovery complete");
        }

        return Decision(InputAction.None, "waiting");
    }

    public StateDecision Stop(DateTimeOffset now)
    {
        var action = ReleaseIfHeld();
        Transition(FishingPhase.Stopped, now);
        return Decision(action, "stop requested");
    }

    private InputAction MinigameAction(DetectionObservation observation)
    {
        if ((observation.TargetYNorm is null && observation.Target is null) ||
            (observation.ControlTopNorm is null && observation.ControlBar is null))
        {
            return ReleaseIfHeld();
        }

        var center = observation.ControlTopNorm is not null && observation.ControlBottomNorm is not null
            ? (observation.ControlTopNorm.Value + observation.ControlBottomNorm.Value) / 2f
            : observation.ControlBar!.Value.CenterY;
        var target = observation.TargetYNorm ?? observation.Target!.Value.CenterY;
        if (target < center - options.VerticalDeadband && !_leftHeld)
        {
            _leftHeld = true;
            return InputAction.Press;
        }
        if (target > center + options.VerticalDeadband && _leftHeld)
        {
            _leftHeld = false;
            return InputAction.Release;
        }
        return InputAction.None;
    }

    private StateDecision Recover(DateTimeOffset now, string reason)
    {
        var action = ReleaseIfHeld();
        Transition(FishingPhase.Recovery, now);
        return Decision(action == InputAction.None ? InputAction.Click : action, reason);
    }

    private InputAction ReleaseIfHeld()
    {
        if (!_leftHeld) return InputAction.None;
        _leftHeld = false;
        return InputAction.Release;
    }

    private StateDecision Decision(InputAction action, string reason) => new(_phase, action, reason, _cycle);

    private void Transition(FishingPhase next, DateTimeOffset now)
    {
        _phase = next;
        _enteredAt = now;
        _evidence.Clear();
    }

    private sealed class Evidence
    {
        public int Prompt { get; private set; }
        public int Ui { get; private set; }
        public int UiLost { get; private set; }
        public int Success { get; private set; }
        public int Failure { get; private set; }

        public void Update(DetectionObservation observation)
        {
            Prompt = observation.HasPrompt ? Prompt + 1 : 0;
            Ui = observation.HasFishingUi ? Ui + 1 : 0;
            UiLost = observation.HasFishingUi ? 0 : UiLost + 1;
            Success = observation.HasSuccess ? Success + 1 : 0;
            Failure = observation.HasFailure ? Failure + 1 : 0;
        }

        public void Clear() => (Prompt, Ui, UiLost, Success, Failure) = (0, 0, 0, 0, 0);
    }
}
