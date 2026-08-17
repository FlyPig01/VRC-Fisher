using System.Diagnostics;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Input;

internal readonly record struct PulseExecutionResult(
    InputExecutionResult Input,
    TimeSpan ActualHold,
    bool Canceled,
    bool ReleaseRequested = false,
    bool TimingOverrun = false,
    bool EmergencyReleased = false,
    DateTimeOffset ReleasedAt = default,
    TimeSpan ReleasedTimestamp = default,
    TimeSpan? PlannedHold = null,
    string ReleaseCause = "-");

internal sealed class PulseReleaseControl
{
    private readonly object _sync = new();
    private TaskCompletionSource _changed = CreateSignal();
    private readonly TimeSpan? _initialReleaseDelay;
    private readonly TimeSpan? _initialRepressDelay;
    private readonly TimeSpan? _initialPlanHorizon;
    private TimeSpan _feedbackTimeout;
    private TimeSpan _minimumDuration;
    private TimeSpan? _releaseDeadline;
    private TimeSpan? _repressDeadline;
    private TimeSpan? _planHorizonDeadline;
    private TimeSpan? _plannedHold;
    private TimeSpan _pressedTimestamp;
    private TimeSpan _lastFeedbackTimestamp;
    private bool _started;
    private bool _isPressed;
    private bool _pressRequested;
    private bool _releaseRequested;
    private bool _repressed;

    public PulseReleaseControl(
        TimeSpan? initialReleaseDelay,
        TimeSpan feedbackTimeout)
        : this(initialReleaseDelay, null, null, feedbackTimeout)
    {
    }

    public PulseReleaseControl(
        TimeSpan? initialReleaseDelay,
        TimeSpan? initialRepressDelay,
        TimeSpan? initialPlanHorizon,
        TimeSpan feedbackTimeout)
    {
        ValidateDelay(initialReleaseDelay, nameof(initialReleaseDelay));
        ValidateDelay(initialRepressDelay, nameof(initialRepressDelay));
        ValidateDelay(initialPlanHorizon, nameof(initialPlanHorizon));
        if (feedbackTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(feedbackTimeout));
        _initialReleaseDelay = initialReleaseDelay;
        _initialRepressDelay = initialRepressDelay;
        _initialPlanHorizon = initialPlanHorizon;
        _feedbackTimeout = feedbackTimeout;
    }

    public void Begin(TimeSpan minimumDuration)
    {
        if (minimumDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumDuration));
        var now = MonotonicNow;
        lock (_sync)
        {
            if (_started) return;
            _started = true;
            _isPressed = true;
            _minimumDuration = minimumDuration;
            _pressedTimestamp = now;
            _lastFeedbackTimestamp = now;
            ApplyDeadlines(now, _initialReleaseDelay, _initialRepressDelay, _initialPlanHorizon);
            SignalChanged();
        }
    }

    public void UpdatePlan(
        bool pressNow,
        TimeSpan? releaseDelay,
        TimeSpan? repressDelay,
        TimeSpan planHorizon,
        TimeSpan feedbackTimeout)
    {
        ValidateDelay(releaseDelay, nameof(releaseDelay));
        ValidateDelay(repressDelay, nameof(repressDelay));
        if (planHorizon <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(planHorizon));
        if (feedbackTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(feedbackTimeout));

        var now = MonotonicNow;
        lock (_sync)
        {
            _feedbackTimeout = feedbackTimeout;
            _lastFeedbackTimestamp = _started ? now : _lastFeedbackTimestamp;
            ApplyDeadlines(now, releaseDelay, repressDelay, planHorizon);
            if (_started && pressNow && !_isPressed)
                _pressRequested = true;
            SignalChanged();
        }
    }

    public void ScheduleAfter(TimeSpan? delay)
    {
        ValidateDelay(delay, nameof(delay));
        var now = MonotonicNow;
        lock (_sync)
        {
            if (_started)
            {
                _releaseDeadline = delay is { } value ? now + value : null;
                _plannedHold = delay;
                _lastFeedbackTimestamp = now;
            }
            SignalChanged();
        }
    }

    public void Touch()
    {
        var now = MonotonicNow;
        lock (_sync)
        {
            if (_started)
                _lastFeedbackTimestamp = now;
            SignalChanged();
        }
    }

    public void RequestRelease()
    {
        lock (_sync)
        {
            _releaseRequested = true;
            _pressRequested = false;
            _repressDeadline = null;
            SignalChanged();
        }
    }

    public PulseControlSnapshot Snapshot()
    {
        var now = MonotonicNow;
        lock (_sync)
        {
            var minimumRemaining = _isPressed && _pressedTimestamp != default
                ? Max(_minimumDuration - (now - _pressedTimestamp), TimeSpan.Zero)
                : TimeSpan.Zero;
            return new(
                _releaseRequested,
                _pressRequested,
                _isPressed,
                _isPressed && _releaseDeadline is { } releaseAt
                    ? releaseAt - now
                    : null,
                !_isPressed && _repressDeadline is { } repressAt
                    ? repressAt - now
                    : null,
                _planHorizonDeadline is { } horizonAt
                    ? horizonAt - now
                    : null,
                _started
                    ? _lastFeedbackTimestamp + _feedbackTimeout - now
                    : _feedbackTimeout,
                minimumRemaining,
                _plannedHold,
                _repressed,
                _changed.Task);
        }
    }

    public void MarkReleased()
    {
        lock (_sync)
        {
            _isPressed = false;
            _releaseDeadline = null;
            SignalChanged();
        }
    }

    public void MarkPressed()
    {
        var now = MonotonicNow;
        lock (_sync)
        {
            _isPressed = true;
            _pressRequested = false;
            _repressDeadline = null;
            _pressedTimestamp = now;
            _repressed = true;
            if (_releaseDeadline is null && _planHorizonDeadline is { } horizon)
            {
                _releaseDeadline = horizon;
                _plannedHold = Max(horizon - now, TimeSpan.Zero);
            }
            SignalChanged();
        }
    }

    private void ApplyDeadlines(
        TimeSpan now,
        TimeSpan? releaseDelay,
        TimeSpan? repressDelay,
        TimeSpan? planHorizon)
    {
        _planHorizonDeadline = planHorizon is { } horizon ? now + horizon : null;
        var boundedRelease = releaseDelay;
        if (_planHorizonDeadline is { } horizonDeadline)
        {
            var horizonRemaining = Max(horizonDeadline - now, TimeSpan.Zero);
            boundedRelease = boundedRelease is { } requested
                ? Min(requested, horizonRemaining)
                : horizonRemaining;
        }
        _releaseDeadline = boundedRelease is { } release ? now + release : null;
        _plannedHold = boundedRelease;
        _repressDeadline = repressDelay is { } repress
            && (_planHorizonDeadline is null || now + repress < _planHorizonDeadline)
                ? now + repress
                : null;
    }

    private void SignalChanged()
    {
        var previous = _changed;
        _changed = CreateSignal();
        previous.TrySetResult();
    }

    private static void ValidateDelay(TimeSpan? value, string parameterName)
    {
        if (value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TimeSpan MonotonicNow =>
        Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;
}

internal readonly record struct PulseControlSnapshot(
    bool ReleaseRequested,
    bool PressRequested,
    bool IsPressed,
    TimeSpan? ReleaseRemaining,
    TimeSpan? RepressRemaining,
    TimeSpan? HorizonRemaining,
    TimeSpan FeedbackRemaining,
    TimeSpan MinimumHoldRemaining,
    TimeSpan? PlannedHold,
    bool Repressed,
    Task Changed);

internal sealed class MinigamePulseExecutor
{
    internal static readonly TimeSpan FeedbackWatchdog = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan TimingOverrunTolerance = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan ForegroundPollInterval = TimeSpan.FromMilliseconds(25);
    private readonly IInputController _inputController;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MinigamePulseExecutor(
        IInputController inputController,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _inputController = inputController;
        _delay = delay ?? Task.Delay;
    }

    public async Task<PulseExecutionResult> ExecuteAsync(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var control = new PulseReleaseControl(duration, FeedbackWatchdog);
        return await ExecuteAsync(duration, control, cancellationToken);
    }

    public async Task<PulseExecutionResult> ExecuteAsync(
        TimeSpan minimumDuration,
        PulseReleaseControl control,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (minimumDuration <= TimeSpan.Zero)
        {
            return new(
                InputExecutionResult.Failure(0, 2, "minimum pulse duration must be positive"),
                TimeSpan.Zero,
                false);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var accounting = new InputAccounting();
            var initialPress = _inputController.PressLeft();
            accounting.Add(initialPress);
            if (!initialPress.Succeeded)
                return new(initialPress, TimeSpan.Zero, false);
            if (initialPress.ExpectedEvents == 0)
            {
                _inputController.ReleaseAll();
                return new(
                    InputExecutionResult.Failure(0, 1, "left button was already held before pulse"),
                    TimeSpan.Zero,
                    false);
            }

            accounting.MarkPressed();
            control.Begin(minimumDuration);
            var canceled = false;
            var releaseCause = PulseReleaseCause.Planned;
            InputExecutionResult? failure = null;
            try
            {
                var outcome = await ExecutePlanAsync(control, accounting, cancellationToken);
                releaseCause = outcome.Cause;
                failure = outcome.Failure;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                releaseCause = PulseReleaseCause.Canceled;
            }
            finally
            {
                if (accounting.IsHeld)
                {
                    var finalRelease = _inputController.ReleaseLeft();
                    accounting.Add(finalRelease);
                    accounting.MarkReleased();
                    control.MarkReleased();
                    if (!finalRelease.Succeeded && failure is null)
                        failure = finalRelease;
                }
            }

            var releasedAt = DateTimeOffset.UtcNow;
            var releasedTimestamp = MonotonicNow;
            var snapshot = control.Snapshot();
            var timingOverrun = !snapshot.Repressed
                && releaseCause == PulseReleaseCause.Planned
                && snapshot.PlannedHold is { } planned
                && accounting.TotalHold > planned + TimingOverrunTolerance;
            var emergencyReleased = releaseCause is PulseReleaseCause.FeedbackTimeout
                or PulseReleaseCause.ForegroundLost;
            var input = failure is { } failed
                ? InputExecutionResult.Failure(
                    accounting.SubmittedEvents,
                    accounting.ExpectedEvents,
                    failed.Error ?? "input plan transition failed")
                : InputExecutionResult.Success(
                    accounting.SubmittedEvents,
                    accounting.ExpectedEvents);
            if (!canceled && failure is null && !_inputController.IsTargetForeground)
            {
                input = InputExecutionResult.Failure(
                    accounting.SubmittedEvents,
                    accounting.ExpectedEvents,
                    "VRChat lost foreground during pulse");
            }

            return new(
                input,
                accounting.TotalHold,
                canceled,
                releaseCause == PulseReleaseCause.Requested,
                timingOverrun,
                emergencyReleased,
                releasedAt,
                releasedTimestamp,
                snapshot.PlannedHold,
                releaseCause.ToString());
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PlanOutcome> ExecutePlanAsync(
        PulseReleaseControl control,
        InputAccounting accounting,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_inputController.IsTargetForeground)
                return new(PulseReleaseCause.ForegroundLost);

            var snapshot = control.Snapshot();
            if (snapshot.FeedbackRemaining <= TimeSpan.Zero)
                return new(PulseReleaseCause.FeedbackTimeout);

            if (snapshot.IsPressed)
            {
                var minimumSatisfied = snapshot.MinimumHoldRemaining <= TimeSpan.Zero;
                if (minimumSatisfied && snapshot.ReleaseRequested)
                    return new(PulseReleaseCause.Requested);
                if (minimumSatisfied
                    && snapshot.ReleaseRemaining is { } releaseRemaining
                    && releaseRemaining <= TimeSpan.Zero)
                {
                    var release = _inputController.ReleaseLeft();
                    accounting.Add(release);
                    if (!release.Succeeded)
                        return new(PulseReleaseCause.Requested, release);
                    accounting.MarkReleased();
                    control.MarkReleased();
                    var released = control.Snapshot();
                    if (!released.PressRequested && released.RepressRemaining is null)
                        return new(PulseReleaseCause.Planned);
                    continue;
                }

                var wait = ForegroundPollInterval;
                if (!minimumSatisfied)
                    wait = Min(wait, snapshot.MinimumHoldRemaining);
                else if (snapshot.ReleaseRemaining is { } plannedRemaining)
                    wait = Min(wait, Max(plannedRemaining, TimeSpan.FromMilliseconds(1)));
                wait = Min(wait, Max(snapshot.FeedbackRemaining, TimeSpan.FromMilliseconds(1)));
                await WaitForChangeAsync(wait, snapshot.Changed, cancellationToken);
                continue;
            }

            if (snapshot.ReleaseRequested)
                return new(PulseReleaseCause.Requested);
            if (snapshot.HorizonRemaining is { } horizon && horizon <= TimeSpan.Zero)
                return new(PulseReleaseCause.Planned);

            var shouldPress = snapshot.PressRequested
                || snapshot.RepressRemaining is { } repressRemaining
                && repressRemaining <= TimeSpan.Zero;
            if (shouldPress)
            {
                var press = _inputController.PressLeft();
                accounting.Add(press);
                if (!press.Succeeded)
                    return new(PulseReleaseCause.Requested, press);
                if (press.ExpectedEvents == 0)
                {
                    return new(
                        PulseReleaseCause.Requested,
                        InputExecutionResult.Failure(0, 1, "left button was already held before re-press"));
                }
                accounting.MarkPressed();
                control.MarkPressed();
                continue;
            }

            if (snapshot.RepressRemaining is null)
                return new(PulseReleaseCause.Planned);

            var releasedWait = Min(
                ForegroundPollInterval,
                Max(snapshot.RepressRemaining.Value, TimeSpan.FromMilliseconds(1)));
            if (snapshot.HorizonRemaining is { } remainingHorizon)
                releasedWait = Min(releasedWait, Max(remainingHorizon, TimeSpan.FromMilliseconds(1)));
            releasedWait = Min(
                releasedWait,
                Max(snapshot.FeedbackRemaining, TimeSpan.FromMilliseconds(1)));
            await WaitForChangeAsync(releasedWait, snapshot.Changed, cancellationToken);
        }
    }

    private async Task WaitForChangeAsync(
        TimeSpan wait,
        Task changed,
        CancellationToken cancellationToken)
    {
        var delay = _delay(wait, cancellationToken);
        await Task.WhenAny(delay, changed);
        if (delay.IsCompleted)
            await delay;
    }

    private static TimeSpan MonotonicNow =>
        Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;

    private readonly record struct PlanOutcome(
        PulseReleaseCause Cause,
        InputExecutionResult? Failure = null);

    private sealed class InputAccounting
    {
        private long _pressedAt;

        public int SubmittedEvents { get; private set; }
        public int ExpectedEvents { get; private set; }
        public bool IsHeld { get; private set; }
        public TimeSpan TotalHold { get; private set; }

        public void Add(InputExecutionResult result)
        {
            SubmittedEvents += result.SubmittedEvents;
            ExpectedEvents += result.ExpectedEvents;
        }

        public void MarkPressed()
        {
            if (IsHeld) return;
            IsHeld = true;
            _pressedAt = Stopwatch.GetTimestamp();
        }

        public void MarkReleased()
        {
            if (!IsHeld) return;
            TotalHold += Stopwatch.GetElapsedTime(_pressedAt);
            IsHeld = false;
            _pressedAt = 0;
        }
    }

    private enum PulseReleaseCause
    {
        Planned,
        Requested,
        FeedbackTimeout,
        ForegroundLost,
        Canceled
    }
}
