namespace VrcFisher.Core;

public sealed class FishingStateMachine : IStateMachine
{
    private StateMachineOptions _options;
    private readonly Evidence _evidence = new();
    private readonly MinigameController _minigameController;
    private FishingPhase _phase = FishingPhase.Idle;
    private DateTimeOffset _enteredAt;
    private int _cycle;
    private long _panelGeneration;
    private bool _fallbackHookPending;
    private bool _fallbackRecovery;

    public FishingStateMachine(
        StateMachineOptions options,
        MinigameDynamicsParameters? initialDynamics = null)
    {
        _options = options;
        _minigameController = new MinigameController(initialDynamics);
    }

    public FishingPhase Phase => _phase;
    public MinigameDynamicsParameters MinigameDynamics => _minigameController.Dynamics;

    public void UpdateBiteFallback(TimeSpan value) =>
        _options = _options with { BiteFallback = value };

    public void Reset(DateTimeOffset now)
    {
        _evidence.Clear();
        _phase = FishingPhase.Idle;
        _enteredAt = now;
        _fallbackHookPending = false;
        _fallbackRecovery = false;
        ResetControlState();
    }

    public StateDecision Step(DetectionObservation observation, DateTimeOffset now)
        => Step(observation, now, MinigameInputState.Released);

    public StateDecision Step(
        DetectionObservation observation,
        DateTimeOffset now,
        MinigameInputState minigameInputState)
        => Step(
            observation,
            now,
            minigameInputState,
            observation.CapturedTimestamp,
            TimeSpan.Zero,
            MinigameController.MinimumPulseDuration);

    public StateDecision Step(
        DetectionObservation observation,
        DateTimeOffset now,
        MinigameInputState minigameInputState,
        TimeSpan controlTimestamp,
        TimeSpan remainingMinimumHold,
        TimeSpan currentMinigameInterval)
    {
        _evidence.Update(observation, _options.BiteIndicatorEvidenceWindow);

        if (_phase == FishingPhase.Idle)
        {
            _cycle++;
            Transition(FishingPhase.Casting, now);
            return Decision(InputAction.Click, "cast");
        }

        var elapsed = now - _enteredAt;
        switch (_phase)
        {
            case FishingPhase.Casting when elapsed >= _options.CastSettle:
                Transition(FishingPhase.WaitingForBite, now);
                break;
            case FishingPhase.WaitingForBite:
                if (_evidence.BiteIndicatorHits >= _options.BiteIndicatorConfirmFrames)
                {
                    _fallbackHookPending = false;
                    Transition(FishingPhase.Hooking, now);
                    return Decision(InputAction.Click, "bite confirmed");
                }
                if (_options.BiteFallback > TimeSpan.Zero && elapsed >= _options.BiteFallback)
                {
                    _fallbackHookPending = true;
                    Transition(FishingPhase.Hooking, now);
                    return Decision(InputAction.Click, "bite fallback");
                }
                if (elapsed >= _options.BiteTimeout) return Recover(now, "bite timeout");
                break;
            case FishingPhase.Hooking:
                if (elapsed >= _options.HookToUiMinimum && _evidence.Ui >= _options.UiConfirmFrames)
                {
                    _fallbackHookPending = false;
                    Transition(FishingPhase.Minigame, now);
                    return Decision(InputAction.None, "minigame confirmed");
                }
                if (elapsed >= _options.BiteToMinigameTimeout)
                {
                    if (_fallbackHookPending)
                        return RecoverFallbackWithoutClick(now);

                    return Recover(now, "minigame did not start");
                }
                break;
            case FishingPhase.Minigame:
                if (HasPanelRelocated(observation))
                {
                    ResetControlState();
                    _panelGeneration = observation.PanelGeneration;
                    return Decision(InputAction.Release, "minigame panel relocated");
                }
                if (observation.PanelGeneration > 0 && _panelGeneration == 0)
                    _panelGeneration = observation.PanelGeneration;
                if (_evidence.UiLost >= _options.UiLostFrames)
                {
                    Transition(FishingPhase.Reeling, now);
                    return Decision(InputAction.Release, "minigame ended");
                }
                if (elapsed >= _options.MinigameTimeout) return Recover(now, "minigame timeout");
                return MinigameDecision(
                    observation,
                    minigameInputState,
                    controlTimestamp,
                    remainingMinimumHold,
                    currentMinigameInterval);
            case FishingPhase.Reeling:
                if (elapsed >= _options.ReelReadyDelay)
                {
                    Transition(FishingPhase.Loot, now);
                    return Decision(InputAction.Click, "reel and collect");
                }
                break;
            case FishingPhase.Loot:
                if (elapsed >= _options.PostReelDelay)
                {
                    Transition(FishingPhase.Idle, now);
                    return Decision(InputAction.None, "next cycle");
                }
                if (elapsed >= _options.LootTimeout)
                {
                    Transition(FishingPhase.Idle, now);
                    return Decision(InputAction.None, "loot timeout");
                }
                break;
            case FishingPhase.Recovery when elapsed >= _options.RecoveryDelay:
                if (_fallbackRecovery)
                {
                    _fallbackRecovery = false;
                    Transition(FishingPhase.WaitingForBite, now);
                    return Decision(InputAction.None, "bite fallback recovery complete");
                }

                Transition(FishingPhase.Idle, now);
                return Decision(InputAction.None, "recovery complete");
        }

        return Decision(InputAction.None, "waiting");
    }

    public StateDecision Stop(DateTimeOffset now)
    {
        Transition(FishingPhase.Stopped, now);
        return Decision(InputAction.None, "stop requested");
    }

    public DateTimeOffset? AcknowledgeInputCompleted(
        StateDecision decision,
        DateTimeOffset completedAt)
    {
        if (_phase != FishingPhase.Loot
            || decision.Phase != FishingPhase.Loot
            || decision.Action != InputAction.Click)
        {
            return null;
        }

        _enteredAt = completedAt;
        return completedAt + _options.PostReelDelay;
    }

    private StateDecision MinigameDecision(
        DetectionObservation observation,
        MinigameInputState inputState,
        TimeSpan controlTimestamp,
        TimeSpan remainingMinimumHold,
        TimeSpan currentMinigameInterval)
    {
        var control = _minigameController.Step(
            observation,
            inputState,
            controlTimestamp,
            remainingMinimumHold,
            currentMinigameInterval);
        return new StateDecision(
            _phase,
            control.Action,
            control.Reason,
            _cycle,
            control.PredictedReleaseDelay,
            control.Diagnostic,
            control.MinimumPulseDuration,
            control.PredictedRepressDelay,
            control.ControlPlanHorizon,
            control.FeedbackTimeout,
            control.HasFreshFeedback);
    }

    private StateDecision Recover(DateTimeOffset now, string reason)
    {
        _fallbackHookPending = false;
        _fallbackRecovery = false;
        Transition(FishingPhase.Recovery, now);
        return Decision(InputAction.None, reason);
    }

    private StateDecision RecoverFallbackWithoutClick(DateTimeOffset now)
    {
        _fallbackHookPending = false;
        _fallbackRecovery = true;
        Transition(FishingPhase.Recovery, now);
        return Decision(InputAction.None, "bite fallback recovery");
    }

    private bool HasPanelRelocated(DetectionObservation observation) =>
        observation.PanelGeneration > 0
        && _panelGeneration > 0
        && observation.PanelGeneration != _panelGeneration;

    private void ResetControlState()
    {
        _panelGeneration = 0;
        _minigameController.Reset();
    }

    private StateDecision Decision(InputAction action, string reason) =>
        new(_phase, action, reason, _cycle);

    private void Transition(FishingPhase next, DateTimeOffset now)
    {
        if (_phase == FishingPhase.Minigame || next == FishingPhase.Minigame)
            ResetControlState();
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
