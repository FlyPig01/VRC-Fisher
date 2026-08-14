namespace VrcFisher.Core;

public sealed class FishingStateMachine(StateMachineOptions options) : IStateMachine
{
    private readonly Evidence _evidence = new();
    private FishingPhase _phase = FishingPhase.Idle;
    private DateTimeOffset _enteredAt;
    private bool _leftHeld;
    private int _cycle;

    public FishingPhase Phase => _phase;

    public void Reset(DateTimeOffset now)
    {
        _evidence.Clear();
        _phase = FishingPhase.Idle;
        _enteredAt = now;
        _leftHeld = false;
    }

    public StateDecision Step(DetectionObservation observation, DateTimeOffset now)
    {
        _evidence.Update(observation, options.BiteIndicatorEvidenceWindow);

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
                if (_evidence.BiteIndicatorHits >= options.BiteIndicatorConfirmFrames)
                {
                    Transition(FishingPhase.Hooking, now);
                    return Decision(InputAction.Click, "bite confirmed");
                }
                if (options.BiteFallback > TimeSpan.Zero && elapsed >= options.BiteFallback)
                {
                    Transition(FishingPhase.Hooking, now);
                    return Decision(InputAction.Click, "bite fallback");
                }
                if (elapsed >= options.BiteTimeout) return Recover(now, "bite timeout");
                break;
            case FishingPhase.Hooking:
                if (elapsed >= options.HookToUiMinimum && _evidence.Ui >= options.UiConfirmFrames)
                {
                    Transition(FishingPhase.Minigame, now);
                    return Decision(InputAction.None, "minigame confirmed");
                }
                if (elapsed >= options.BiteToMinigameTimeout) return Recover(now, "minigame did not start");
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
        var catchZoneCenter = observation.CatchZoneTopNorm is not null && observation.CatchZoneBottomNorm is not null
            ? (observation.CatchZoneTopNorm.Value + observation.CatchZoneBottomNorm.Value) / 2f
            : observation.CatchZone?.CenterY;
        var targetCenter = observation.MovingTargetYNorm ?? observation.MovingTarget?.CenterY;
        if (catchZoneCenter is null || targetCenter is null)
            return ReleaseIfHeld();

        if (targetCenter < catchZoneCenter - options.VerticalDeadband && !_leftHeld)
        {
            _leftHeld = true;
            return InputAction.Press;
        }
        if (targetCenter > catchZoneCenter + options.VerticalDeadband && _leftHeld)
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
        return Decision(action, reason);
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
        private readonly Queue<bool> _biteIndicatorEvidence = new();
        public int BiteIndicatorHits => _biteIndicatorEvidence.Count(item => item);
        public int Ui { get; private set; }
        public int UiLost { get; private set; }

        public void Update(DetectionObservation observation, int biteIndicatorEvidenceWindow = 5)
        {
            _biteIndicatorEvidence.Enqueue(observation.HasBiteIndicator);
            while (_biteIndicatorEvidence.Count > biteIndicatorEvidenceWindow)
                _biteIndicatorEvidence.Dequeue();
            Ui = observation.HasMinigamePanel ? Ui + 1 : 0;
            UiLost = observation.HasMinigamePanel ? 0 : UiLost + 1;
        }

        public void Clear()
        {
            _biteIndicatorEvidence.Clear();
            (Ui, UiLost) = (0, 0);
        }
    }
}
