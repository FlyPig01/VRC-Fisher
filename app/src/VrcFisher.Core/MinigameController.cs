using System.Globalization;

namespace VrcFisher.Core;

internal readonly record struct MinigameControlDecision(
    InputAction Action,
    TimeSpan MinimumPulseDuration,
    TimeSpan? PredictedReleaseDelay,
    TimeSpan? PredictedRepressDelay,
    TimeSpan ControlPlanHorizon,
    TimeSpan FeedbackTimeout,
    bool HasFreshFeedback,
    string Reason,
    string? Diagnostic = null);

internal sealed class MinigameController
{
    internal static readonly TimeSpan MinimumPulseDuration = TimeSpan.FromMilliseconds(60);
    internal static readonly TimeSpan MaximumControlFrameAge = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan InitialReleaseEstimate = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan MinimumFeedbackTimeout = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan MaximumFeedbackTimeout = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan FeedbackTimeoutAllowance = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan MinimumReleasedSegment = TimeSpan.FromMilliseconds(20);

    private const int MotionFrameCapacity = 3;
    private const int RecentValueCapacity = 5;
    private const int ControlIntervalCapacity = 30;
    private const int RequiredAccelerationSamples = 3;
    private const double MinimumAccelerationMagnitude = 0.1;
    private const double MaximumAccelerationMagnitude = 40.0;
    private const double MaximumHeightVariationRatio = 0.20;
    private static readonly TimeSpan MinimumDynamicsInterval = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan MaximumDynamicsInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ReleasePlanResolution = TimeSpan.FromMilliseconds(5);
    private const double DecisionHysteresisRatio = 0.02;

    private readonly Queue<MotionFrame> _motionFrames = new();
    private readonly Queue<TimeSpan> _controlIntervals = new();
    private readonly Queue<double> _zoneHeights = new();
    private readonly Queue<double> _releaseAccelerationSamples = new();
    private readonly Queue<double> _pressAccelerationSamples = new();
    private double? _releaseAcceleration;
    private double? _pressAcceleration;
    private long _lastFrameNumber = long.MinValue;
    private TimeSpan _lastCapturedTimestamp;
    private TimeSpan _lastControlTimestamp;
    private int _consecutiveMissingFrames;

    public MinigameController(MinigameDynamicsParameters? initialDynamics = null)
    {
        var dynamics = (initialDynamics ?? MinigameDynamicsParameters.Empty).Normalize();
        _releaseAcceleration = dynamics.ReleaseAcceleration;
        _pressAcceleration = dynamics.PressAcceleration;
    }

    public MinigameDynamicsParameters Dynamics => new(
        _releaseAcceleration,
        _pressAcceleration);

    public MinigameControlDecision Step(
        DetectionObservation observation,
        MinigameInputState inputState,
        TimeSpan controlTimestamp,
        TimeSpan remainingMinimumHold,
        TimeSpan currentMinigameInterval,
        MinigameInputTimeline? inputTimeline = null)
    {
        if (observation.CatchZone is not { } catchZone
            || observation.MovingTarget is not { } movingTarget)
        {
            _consecutiveMissingFrames++;
            if (_motionFrames.Count > 0 && _consecutiveMissingFrames == 1)
            {
                var missingFeedbackTime = FeedbackTime(currentMinigameInterval);
                return new(
                    InputAction.None,
                    TimeSpan.Zero,
                    null,
                    null,
                    missingFeedbackTime,
                    FeedbackTimeout(missingFeedbackTime),
                    false,
                    "single control miss; keep bounded plan");
            }

            Reset();
            return new(
                InputAction.Release,
                TimeSpan.Zero,
                null,
                null,
                TimeSpan.Zero,
                TimeSpan.Zero,
                false,
                "control target missing");
        }

        if (!IsFresh(observation))
            return new(
                InputAction.None,
                TimeSpan.Zero,
                null,
                null,
                TimeSpan.Zero,
                TimeSpan.Zero,
                false,
                "waiting for fresh control frame");

        var frameAge = controlTimestamp - observation.CapturedTimestamp;
        if (frameAge < TimeSpan.Zero || frameAge > MaximumControlFrameAge)
        {
            Reset();
            return new(
                InputAction.Release,
                TimeSpan.Zero,
                null,
                null,
                TimeSpan.Zero,
                TimeSpan.Zero,
                false,
                "control frame stale",
                CreateInvalidFrameDiagnostic(frameAge, inputState));
        }

        _consecutiveMissingFrames = 0;
        var effectiveTimeline = inputTimeline ?? MinigameInputTimeline.Constant(inputState);
        inputState = effectiveTimeline.FinalState;
        RecordControlInterval(controlTimestamp);
        var frame = MotionFrame.From(
            observation,
            catchZone,
            movingTarget,
            effectiveTimeline.InitialState);
        RecordFrame(frame);

        var referenceHeight = ReferenceHeight(catchZone.Height);
        MeasureAcceleration(frame, referenceHeight);
        var zoneVelocity = Velocity(referenceHeight, target: false);
        var targetVelocity = Velocity(referenceHeight, target: true);
        var bounds = ControlBounds.From(frame);
        var feedbackTime = FeedbackTime(currentMinigameInterval);
        var currentZone = AdvanceWithInputTimeline(
            frame.ZoneCenterUp,
            zoneVelocity,
            frame.CapturedTimestamp,
            controlTimestamp,
            effectiveTimeline,
            referenceHeight);
        var currentTarget = Advance(
            frame.TargetCenterUp,
            targetVelocity,
            null,
            frameAge,
            referenceHeight);
        // Relative motion keeps the existing zone trajectory planner while making the target part of every prediction.
        var current = new MotionState(
            currentZone.Position - currentTarget.Position,
            currentZone.Velocity - currentTarget.Velocity);
        var projection = Decide(
            current,
            bounds,
            inputState,
            remainingMinimumHold,
            feedbackTime,
            referenceHeight);

        var minimumPulseDuration = projection.Action == InputAction.Pulse
            ? MinimumPulseDuration
            : TimeSpan.Zero;
        var targetAtFeedback = Advance(
            currentTarget,
            null,
            feedbackTime,
            referenceHeight);

        return new(
            projection.Action,
            minimumPulseDuration,
            projection.PredictedReleaseDelay,
            projection.PredictedRepressDelay,
            feedbackTime,
            FeedbackTimeout(feedbackTime),
            true,
            "follow target",
            CreateDiagnostic(
                zoneVelocity,
                targetVelocity,
                frameAge,
                feedbackTime,
                bounds,
                currentZone,
                currentTarget,
                targetAtFeedback,
                current,
                projection,
                inputState,
                minimumPulseDuration,
                referenceHeight,
                effectiveTimeline));
    }

    public void Reset()
    {
        _motionFrames.Clear();
        _controlIntervals.Clear();
        _zoneHeights.Clear();
        _releaseAccelerationSamples.Clear();
        _pressAccelerationSamples.Clear();
        _lastFrameNumber = long.MinValue;
        _lastCapturedTimestamp = default;
        _lastControlTimestamp = default;
        _consecutiveMissingFrames = 0;
    }

    private bool IsFresh(DetectionObservation observation) =>
        observation.FrameNumber > _lastFrameNumber
        && (_lastCapturedTimestamp == default
            || observation.CapturedTimestamp > _lastCapturedTimestamp);

    private void RecordFrame(MotionFrame frame)
    {
        if (frame.ZoneHeight > 0)
            Enqueue(_zoneHeights, frame.ZoneHeight);
        Enqueue(_motionFrames, frame, MotionFrameCapacity);
        _lastFrameNumber = frame.FrameNumber;
        _lastCapturedTimestamp = frame.CapturedTimestamp;
    }

    private void RecordControlInterval(TimeSpan controlTimestamp)
    {
        if (_lastControlTimestamp != default)
        {
            var interval = controlTimestamp - _lastControlTimestamp;
            if (interval > TimeSpan.Zero)
                Enqueue(_controlIntervals, interval, ControlIntervalCapacity);
        }

        _lastControlTimestamp = controlTimestamp;
    }

    private double ReferenceHeight(double fallback)
    {
        var heights = _zoneHeights.Where(value => value > 0).Order().ToArray();
        return heights.Length == 0 ? Math.Max(1.0, fallback) : Median(heights);
    }

    private double Velocity(double referenceHeight, bool target)
    {
        if (_motionFrames.Count < 2 || referenceHeight <= 0) return 0.0;
        var frames = _motionFrames.ToArray();
        var first = frames[0];
        var last = frames[^1];
        var seconds = (last.CapturedTimestamp - first.CapturedTimestamp).TotalSeconds;
        var firstPosition = target ? first.TargetCenterUp : first.ZoneCenterUp;
        var lastPosition = target ? last.TargetCenterUp : last.ZoneCenterUp;
        return seconds <= 0
            ? 0.0
            : (lastPosition - firstPosition) / referenceHeight / seconds;
    }

    private void MeasureAcceleration(MotionFrame frame, double referenceHeight)
    {
        MeasureContinuousAcceleration(referenceHeight);
    }

    private void MeasureContinuousAcceleration(double referenceHeight)
    {
        if (_motionFrames.Count < 3 || referenceHeight <= 0) return;

        var frames = _motionFrames.ToArray();
        var first = frames[0];
        var middle = frames[1];
        var last = frames[2];
        if (first.InputState != middle.InputState
            || middle.InputState != last.InputState
            || last.InputState == MinigameInputState.Cooldown)
        {
            return;
        }

        var firstInterval = middle.CapturedTimestamp - first.CapturedTimestamp;
        var secondInterval = last.CapturedTimestamp - middle.CapturedTimestamp;
        if (!ValidDynamicsInterval(firstInterval)
            || !ValidDynamicsInterval(secondInterval)
            || !StableHeights(first.ZoneHeight, middle.ZoneHeight, last.ZoneHeight, referenceHeight))
        {
            return;
        }

        var firstDisplacement = (middle.ZoneCenterUp - first.ZoneCenterUp) / referenceHeight;
        var secondDisplacement = (last.ZoneCenterUp - middle.ZoneCenterUp) / referenceHeight;

        var firstVelocity = firstDisplacement / firstInterval.TotalSeconds;
        var secondVelocity = secondDisplacement / secondInterval.TotalSeconds;
        var velocityInterval = (firstInterval + secondInterval).TotalSeconds / 2.0;
        if (velocityInterval <= 0) return;

        AddAccelerationSample(
            last.InputState,
            (secondVelocity - firstVelocity) / velocityInterval);
    }


    private bool AddAccelerationSample(
        MinigameInputState inputState,
        double acceleration)
    {
        if (!double.IsFinite(acceleration)
            || Math.Abs(acceleration) < MinimumAccelerationMagnitude
            || Math.Abs(acceleration) > MaximumAccelerationMagnitude)
        {
            return false;
        }

        Queue<double> samples;
        if (inputState == MinigameInputState.Released && acceleration < 0)
            samples = _releaseAccelerationSamples;
        else if (inputState == MinigameInputState.Pressed && acceleration > 0)
            samples = _pressAccelerationSamples;
        else
            return false;

        Enqueue(samples, acceleration);
        if (samples.Count < RequiredAccelerationSamples) return true;

        var measured = Median(samples.Order().ToArray());
        if (inputState == MinigameInputState.Released)
            _releaseAcceleration = measured;
        else
            _pressAcceleration = measured;
        return true;
    }

    private TimeSpan FeedbackTime(TimeSpan configuredInterval)
    {
        var measured = _controlIntervals.Count > 0
            ? Percentile95(_controlIntervals)
            : configuredInterval;
        var feedback = Max(measured, configuredInterval);
        return feedback > MaximumControlFrameAge ? MaximumControlFrameAge : feedback;
    }

    private static TimeSpan FeedbackTimeout(TimeSpan feedbackTime)
    {
        var timeout = feedbackTime + feedbackTime + FeedbackTimeoutAllowance;
        if (timeout < MinimumFeedbackTimeout) return MinimumFeedbackTimeout;
        return timeout > MaximumFeedbackTimeout ? MaximumFeedbackTimeout : timeout;
    }

    private double? AccelerationFor(MinigameInputState inputState) => inputState switch
    {
        MinigameInputState.Pressed => _pressAcceleration,
        MinigameInputState.Released => _releaseAcceleration,
        _ => null
    };

    private MotionState AdvanceWithInputTimeline(
        double position,
        double velocity,
        TimeSpan capturedTimestamp,
        TimeSpan controlTimestamp,
        MinigameInputTimeline timeline,
        double referenceHeight)
    {
        var state = new MotionState(position, velocity);
        var cursor = capturedTimestamp;
        var inputState = timeline.InitialState;
        foreach (var transition in timeline.Transitions)
        {
            if (transition.Timestamp <= capturedTimestamp)
            {
                inputState = transition.State;
                continue;
            }
            if (transition.Timestamp > controlTimestamp) break;
            state = Advance(
                state,
                AccelerationFor(inputState),
                transition.Timestamp - cursor,
                referenceHeight);
            cursor = transition.Timestamp;
            inputState = transition.State;
        }
        return Advance(
            state,
            AccelerationFor(inputState),
            controlTimestamp - cursor,
            referenceHeight);
    }

    private double? PressAccelerationForBraking()
    {
        if (_pressAccelerationSamples.Count < RequiredAccelerationSamples)
            return _pressAcceleration;

        var samples = _pressAccelerationSamples.Order().ToArray();
        var index = Math.Max(0, (int)Math.Ceiling(samples.Length * 0.25) - 1);
        return samples[index];
    }

    private ControlProjection Decide(
        MotionState current,
        ControlBounds bounds,
        MinigameInputState inputState,
        TimeSpan remainingMinimumHold,
        TimeSpan feedbackTime,
        double referenceHeight)
    {
        if (!bounds.IsValid)
            return ControlProjection.Release(current.Position, "geometry impossible");

        var brakingPressAcceleration = PressAccelerationForBraking();
        var waitEnvelope = SimulateWaitCandidate(
            current,
            _releaseAcceleration,
            brakingPressAcceleration,
            feedbackTime,
            referenceHeight);
        var waitMargin = BoundaryMargin(waitEnvelope.Minimum, waitEnvelope.Maximum, bounds);
        var hysteresis = referenceHeight * DecisionHysteresisRatio;
        var isContained = current.Position >= bounds.Lower
            && current.Position <= bounds.Upper;

        if (inputState != MinigameInputState.Cooldown
            && current.Position > bounds.Upper
            && waitEnvelope.Minimum >= bounds.Lower)
        {
            return ReleaseWithEnvelope(
                waitEnvelope,
                waitMargin,
                "catch zone above containment range",
                brakingPressAcceleration);
        }

        if (inputState == MinigameInputState.Released
            && isContained
            && ((current.Velocity >= 0 && waitEnvelope.Maximum <= bounds.Upper)
                || (current.Velocity < 0 && waitEnvelope.Minimum >= bounds.Lower)))
        {
            return ReleaseWithEnvelope(
                waitEnvelope,
                waitMargin,
                "target contained; keep released",
                brakingPressAcceleration);
        }

        if (inputState == MinigameInputState.Pressed)
        {
            if (brakingPressAcceleration is not > 0)
            {
                var releaseIsAlreadyUseful =
                    (isContained
                        && (current.Velocity >= 0
                            || waitEnvelope.Minimum >= bounds.Lower))
                    || (current.Velocity > 0
                        && waitEnvelope.AtFeedback.Position >= bounds.Lower);
                return new(
                    releaseIsAlreadyUseful ? InputAction.Release : InputAction.None,
                    releaseIsAlreadyUseful
                        ? "release while press acceleration is unavailable"
                        : "continue pulse while press acceleration is measured",
                    releaseIsAlreadyUseful ? null : InitialReleaseEstimate,
                    null,
                    waitEnvelope.AtFeedback.Position,
                    waitEnvelope.Maximum,
                    waitEnvelope.AtFeedback.Position,
                    waitEnvelope.Maximum,
                    waitEnvelope.Minimum,
                    waitEnvelope.Minimum,
                    waitEnvelope.Maximum,
                    Math.Max(0, -waitMargin),
                    Math.Max(0, -waitMargin),
                    waitMargin,
                    waitMargin,
                    null);
            }

            var earliestDelay = Max(remainingMinimumHold, TimeSpan.Zero);
            var releaseNowEnvelope = SimulatePulsePlan(
                current,
                brakingPressAcceleration,
                _releaseAcceleration,
                earliestDelay,
                feedbackTime,
                referenceHeight);
            var releaseNowMargin = BoundaryMargin(
                releaseNowEnvelope.Minimum,
                releaseNowEnvelope.Maximum,
                bounds);
            if (isContained
                && (current.Velocity >= 0
                    || releaseNowEnvelope.Minimum >= bounds.Lower))
            {
                return ReleaseWithEnvelope(
                    releaseNowEnvelope,
                    releaseNowMargin,
                    "target contained; release pulse",
                    brakingPressAcceleration);
            }
            var releaseNowRepress = FindPredictedRepressPlan(
                current,
                bounds,
                brakingPressAcceleration.Value,
                _releaseAcceleration,
                earliestDelay,
                feedbackTime,
                referenceHeight);
            if (releaseNowRepress is { } releaseRepress)
                releaseNowEnvelope = releaseRepress.Envelope;
            var unconstrainedContinueDelay = FindPredictedReleaseDelay(
                current,
                bounds,
                brakingPressAcceleration.Value,
                _releaseAcceleration,
                earliestDelay + ReleasePlanResolution,
                referenceHeight);
            var constrainedContinueDelay = ConstrainReleaseDelayForUpperBoundary(
                current,
                bounds.Upper,
                brakingPressAcceleration.Value,
                _releaseAcceleration,
                earliestDelay,
                unconstrainedContinueDelay,
                referenceHeight);
            var continueAllowed = constrainedContinueDelay is not null;
            var continueDelay = constrainedContinueDelay ?? earliestDelay;
            var continueEnvelope = SimulatePulsePlan(
                current,
                brakingPressAcceleration,
                _releaseAcceleration,
                continueDelay,
                feedbackTime,
                referenceHeight);
            var continueRepress = continueAllowed
                ? FindPredictedRepressPlan(
                    current,
                    bounds,
                    brakingPressAcceleration.Value,
                    _releaseAcceleration,
                    continueDelay,
                    feedbackTime,
                    referenceHeight)
                : null;
            if (continueRepress is { } continuedRepress)
                continueEnvelope = continuedRepress.Envelope;
            var releaseMargin = releaseNowMargin;
            var continueMargin = BoundaryMargin(
                continueEnvelope.Minimum,
                continueEnvelope.Maximum,
                bounds);
            var bothSafe = releaseMargin >= 0 && continueMargin >= 0;
            var continuePressed = continueAllowed
                && (bothSafe
                    ? continueMargin + hysteresis >= releaseMargin
                    : continueMargin >= releaseMargin);
            return new(
                continuePressed ? InputAction.None : InputAction.Release,
                !continueAllowed
                    ? "upper boundary requires earliest release"
                    : continuePressed
                    ? "continue pulse has greater two-sided margin"
                    : "release now has greater two-sided margin",
                continuePressed
                    ? continueDelay
                    : releaseNowRepress is not null
                        ? earliestDelay
                        : null,
                continuePressed
                    ? continueRepress?.Delay
                    : releaseNowRepress?.Delay,
                releaseNowEnvelope.AtFeedback.Position,
                releaseNowEnvelope.Maximum,
                continueEnvelope.AtFeedback.Position,
                continueEnvelope.Maximum,
                releaseNowEnvelope.Minimum,
                continueEnvelope.Minimum,
                continueEnvelope.Maximum,
                Math.Max(0, -releaseMargin),
                Math.Max(0, -continueMargin),
                releaseMargin,
                continueMargin,
                brakingPressAcceleration);
        }

        if (inputState == MinigameInputState.Cooldown)
        {
            return new(
                InputAction.None,
                "pulse cooldown",
                null,
                null,
                waitEnvelope.AtFeedback.Position,
                waitEnvelope.Maximum,
                waitEnvelope.AtFeedback.Position,
                waitEnvelope.Maximum,
                waitEnvelope.Minimum,
                waitEnvelope.Minimum,
                waitEnvelope.Maximum,
                Math.Max(0, -waitMargin),
                Math.Max(0, -waitMargin),
                waitMargin,
                waitMargin,
                brakingPressAcceleration);
        }

        var plannedDelay = brakingPressAcceleration is > 0
            ? FindPredictedReleaseDelay(
                current,
                bounds,
                brakingPressAcceleration.Value,
                _releaseAcceleration,
                MinimumPulseDuration,
                referenceHeight)
            : InitialReleaseEstimate;
        var pulseAllowed = true;
        if (brakingPressAcceleration is > 0)
        {
            var constrainedDelay = ConstrainReleaseDelayForUpperBoundary(
                current,
                bounds.Upper,
                brakingPressAcceleration.Value,
                _releaseAcceleration,
                MinimumPulseDuration,
                plannedDelay,
                referenceHeight);
            pulseAllowed = constrainedDelay is not null;
            plannedDelay = constrainedDelay ?? MinimumPulseDuration;
        }
        var pulseEnvelope = SimulatePulsePlan(
            current,
            brakingPressAcceleration,
            _releaseAcceleration,
            plannedDelay,
            feedbackTime,
            referenceHeight);
        var pulseRepress = pulseAllowed && brakingPressAcceleration is > 0
            ? FindPredictedRepressPlan(
                current,
                bounds,
                brakingPressAcceleration.Value,
                _releaseAcceleration,
                plannedDelay,
                feedbackTime,
                referenceHeight)
            : null;
        if (pulseRepress is { } repressedPulse)
            pulseEnvelope = repressedPulse.Envelope;
        var pulseMargin = BoundaryMargin(
            pulseEnvelope.Minimum,
            pulseEnvelope.Maximum,
            bounds);

        if (brakingPressAcceleration is not > 0)
        {
            var releaseIsUseful = waitMargin >= 0
                || (_releaseAcceleration is null
                    && current.Position >= bounds.Lower
                    && current.Velocity >= 0)
                || (current.Velocity > 0
                    && waitEnvelope.AtFeedback.Position >= bounds.Lower);
            var action = releaseIsUseful ? InputAction.Release : InputAction.Pulse;
            return new(
                action,
                action == InputAction.Pulse
                    ? "press acceleration unavailable; start measured pulse"
                    : "press acceleration unavailable; keep released",
                action == InputAction.Pulse ? plannedDelay : null,
                null,
                waitEnvelope.AtFeedback.Position,
                waitEnvelope.Maximum,
                pulseEnvelope.AtFeedback.Position,
                pulseEnvelope.Maximum,
                waitEnvelope.Minimum,
                pulseEnvelope.Minimum,
                pulseEnvelope.Maximum,
                Math.Max(0, -waitMargin),
                Math.Max(0, -pulseMargin),
                waitMargin,
                pulseMargin,
                null);
        }

        var bothCandidatesSafe = waitMargin >= 0 && pulseMargin >= 0;
        var startPulse = pulseAllowed
            && (bothCandidatesSafe
                ? pulseMargin > waitMargin + hysteresis
                : pulseMargin > waitMargin);
        return new(
            startPulse ? InputAction.Pulse : InputAction.Release,
            !pulseAllowed
                ? "minimum pulse crosses upper boundary"
                : startPulse
                ? "pulse candidate has greater two-sided margin"
                : "release candidate retained by two-sided margin",
            startPulse ? plannedDelay : null,
            startPulse ? pulseRepress?.Delay : null,
            waitEnvelope.AtFeedback.Position,
            waitEnvelope.Maximum,
            pulseEnvelope.AtFeedback.Position,
            pulseEnvelope.Maximum,
            waitEnvelope.Minimum,
            pulseEnvelope.Minimum,
            pulseEnvelope.Maximum,
            Math.Max(0, -waitMargin),
            Math.Max(0, -pulseMargin),
            waitMargin,
            pulseMargin,
            brakingPressAcceleration);
    }

    private static RepressPlan? FindPredictedRepressPlan(
        MotionState current,
        ControlBounds bounds,
        double pressAcceleration,
        double? releaseAcceleration,
        TimeSpan releaseDelay,
        TimeSpan feedbackTime,
        double referenceHeight)
    {
        if (pressAcceleration <= 0
            || releaseAcceleration is not < 0
            || feedbackTime <= TimeSpan.Zero)
        {
            return null;
        }

        var withoutRepress = SimulatePulsePlan(
            current,
            pressAcceleration,
            releaseAcceleration,
            releaseDelay,
            feedbackTime,
            referenceHeight);
        if (withoutRepress.Minimum >= bounds.Lower)
            return null;

        var earliest = releaseDelay + MinimumReleasedSegment;
        var latest = feedbackTime - MinimumPulseDuration;
        if (earliest > latest)
            return null;

        var baselineMargin = BoundaryMargin(
            withoutRepress.Minimum,
            withoutRepress.Maximum,
            bounds);
        RepressPlan? best = null;
        for (var delay = earliest;
             delay <= latest;
             delay += ReleasePlanResolution)
        {
            var envelope = SimulatePulseRepressPlan(
                current,
                pressAcceleration,
                releaseAcceleration.Value,
                releaseDelay,
                delay,
                feedbackTime,
                referenceHeight);
            if (!RepressPlanStaysBelowUpperBoundary(envelope, bounds.Upper))
                continue;
            var margin = BoundaryMargin(envelope.Minimum, envelope.Maximum, bounds);
            if (best is null || margin > best.Value.Margin)
                best = new RepressPlan(delay, envelope, margin);
        }

        return best is { } candidate
               && candidate.Margin > baselineMargin + referenceHeight * 0.01
            ? candidate
            : null;
    }

    private static TimeSpan FindPredictedReleaseDelay(
        MotionState current,
        ControlBounds bounds,
        double pressAcceleration,
        double? releaseAcceleration,
        TimeSpan minimumDelay,
        double referenceHeight)
    {
        var target = Math.Min(
            bounds.Upper,
            bounds.Lower + referenceHeight * DecisionHysteresisRatio);
        var lowerSeconds = Math.Max(0, minimumDelay.TotalSeconds);
        if (ReleasePeakAfterPress(
                current,
                pressAcceleration,
                releaseAcceleration,
                lowerSeconds,
                referenceHeight) >= target)
        {
            return TimeSpan.FromSeconds(lowerSeconds);
        }

        var upperSeconds = Math.Max(
            lowerSeconds + ReleasePlanResolution.TotalSeconds,
            InitialReleaseEstimate.TotalSeconds);
        for (var expansion = 0;
             expansion < 32 && ReleasePeakAfterPress(
                   current,
                   pressAcceleration,
                   releaseAcceleration,
                   upperSeconds,
                   referenceHeight) < target;
             expansion++)
        {
            upperSeconds *= 2.0;
        }

        for (var index = 0; index < 24; index++)
        {
            var middle = (lowerSeconds + upperSeconds) / 2.0;
            if (ReleasePeakAfterPress(
                    current,
                    pressAcceleration,
                    releaseAcceleration,
                    middle,
                    referenceHeight) < target)
            {
                lowerSeconds = middle;
            }
            else
            {
                upperSeconds = middle;
            }
        }

        return TimeSpan.FromSeconds(upperSeconds);
    }

    internal static TimeSpan? ConstrainReleaseDelayForUpperBoundary(
        double position,
        double velocity,
        double upperBoundary,
        double pressAcceleration,
        double? releaseAcceleration,
        TimeSpan earliestReleaseDelay,
        TimeSpan plannedReleaseDelay,
        double referenceHeight) => ConstrainReleaseDelayForUpperBoundary(
            new MotionState(position, velocity),
            upperBoundary,
            pressAcceleration,
            releaseAcceleration,
            earliestReleaseDelay,
            plannedReleaseDelay,
            referenceHeight);

    private static TimeSpan? ConstrainReleaseDelayForUpperBoundary(
        MotionState current,
        double upperBoundary,
        double pressAcceleration,
        double? releaseAcceleration,
        TimeSpan earliestReleaseDelay,
        TimeSpan plannedReleaseDelay,
        double referenceHeight)
    {
        var earliestSeconds = Math.Max(0, earliestReleaseDelay.TotalSeconds);
        var plannedSeconds = Math.Max(earliestSeconds, plannedReleaseDelay.TotalSeconds);
        if (MaximumAfterPressAndRelease(
                current,
                pressAcceleration,
                releaseAcceleration,
                plannedSeconds,
                referenceHeight) <= upperBoundary)
        {
            return TimeSpan.FromSeconds(plannedSeconds);
        }

        if (MaximumAfterPressAndRelease(
                current,
                pressAcceleration,
                releaseAcceleration,
                earliestSeconds,
                referenceHeight) > upperBoundary)
        {
            return null;
        }

        var safeSeconds = earliestSeconds;
        var unsafeSeconds = plannedSeconds;
        for (var index = 0; index < 24; index++)
        {
            var middle = (safeSeconds + unsafeSeconds) / 2.0;
            if (MaximumAfterPressAndRelease(
                    current,
                    pressAcceleration,
                    releaseAcceleration,
                    middle,
                    referenceHeight) <= upperBoundary)
            {
                safeSeconds = middle;
            }
            else
            {
                unsafeSeconds = middle;
            }
        }

        return TimeSpan.FromSeconds(safeSeconds);
    }

    private static double MaximumAfterPressAndRelease(
        MotionState current,
        double pressAcceleration,
        double? releaseAcceleration,
        double pressSeconds,
        double referenceHeight) => Math.Max(
            current.Position,
            ReleasePeakAfterPress(
                current,
                pressAcceleration,
                releaseAcceleration,
                pressSeconds,
                referenceHeight));

    private static bool RepressPlanStaysBelowUpperBoundary(
        MotionEnvelope envelope,
        double upperBoundary) => envelope.Maximum <= upperBoundary;

    private static double ReleasePeakAfterPress(
        MotionState current,
        double pressAcceleration,
        double? releaseAcceleration,
        double pressSeconds,
        double referenceHeight)
    {
        var atRelease = Advance(
            current,
            pressAcceleration,
            TimeSpan.FromSeconds(pressSeconds),
            referenceHeight);
        return UpwardPeak(atRelease, releaseAcceleration, referenceHeight);
    }

    private static MotionEnvelope SimulateWaitCandidate(
        MotionState current,
        double? releaseAcceleration,
        double? pressAcceleration,
        TimeSpan feedbackTime,
        double referenceHeight)
    {
        var releaseSegment = SimulateSegment(
            current,
            releaseAcceleration,
            feedbackTime,
            referenceHeight);
        var delayedBrakeMinimum = DownwardStop(
            releaseSegment.End,
            pressAcceleration,
            referenceHeight);
        var releasePeak = UpwardPeak(current, releaseAcceleration, referenceHeight);
        return new(
            releaseSegment.End,
            Math.Min(releaseSegment.Minimum, delayedBrakeMinimum),
            Math.Max(releaseSegment.Maximum, releasePeak));
    }

    private static MotionEnvelope SimulatePulsePlan(
        MotionState current,
        double? pressAcceleration,
        double? releaseAcceleration,
        TimeSpan releaseDelay,
        TimeSpan feedbackTime,
        double referenceHeight)
    {
        var pressEnvelope = SimulateSegment(
            current,
            pressAcceleration,
            releaseDelay,
            referenceHeight);
        var atFeedback = feedbackTime <= releaseDelay
            ? Advance(current, pressAcceleration, feedbackTime, referenceHeight)
            : Advance(
                pressEnvelope.End,
                releaseAcceleration,
                feedbackTime - releaseDelay,
                referenceHeight);
        var tailDuration = feedbackTime > releaseDelay
            ? feedbackTime - releaseDelay
            : TimeSpan.Zero;
        var tailEnvelope = SimulateSegment(
            pressEnvelope.End,
            releaseAcceleration,
            tailDuration,
            referenceHeight);
        var releasePeak = UpwardPeak(
            pressEnvelope.End,
            releaseAcceleration,
            referenceHeight);
        var delayedBrakeMinimum = DownwardStop(
            atFeedback,
            pressAcceleration,
            referenceHeight);
        return new(
            atFeedback,
            Math.Min(
                Math.Min(pressEnvelope.Minimum, tailEnvelope.Minimum),
                delayedBrakeMinimum),
            Math.Max(Math.Max(pressEnvelope.Maximum, tailEnvelope.Maximum), releasePeak));
    }

    private static MotionEnvelope SimulatePulseRepressPlan(
        MotionState current,
        double pressAcceleration,
        double releaseAcceleration,
        TimeSpan releaseDelay,
        TimeSpan repressDelay,
        TimeSpan feedbackTime,
        double referenceHeight)
    {
        var firstPress = SimulateSegment(
            current,
            pressAcceleration,
            releaseDelay,
            referenceHeight);
        var released = SimulateSegment(
            firstPress.End,
            releaseAcceleration,
            repressDelay - releaseDelay,
            referenceHeight);
        var secondPress = SimulateSegment(
            released.End,
            pressAcceleration,
            feedbackTime - repressDelay,
            referenceHeight);
        var finalPeak = UpwardPeak(
            secondPress.End,
            releaseAcceleration,
            referenceHeight);
        return new(
            secondPress.End,
            Math.Min(firstPress.Minimum, Math.Min(released.Minimum, secondPress.Minimum)),
            Math.Max(
                Math.Max(firstPress.Maximum, Math.Max(released.Maximum, secondPress.Maximum)),
                finalPeak));
    }

    private static MotionSegment SimulateSegment(
        MotionState current,
        double? acceleration,
        TimeSpan duration,
        double referenceHeight)
    {
        var end = Advance(current, acceleration, duration, referenceHeight);
        var minimum = Math.Min(current.Position, end.Position);
        var maximum = Math.Max(current.Position, end.Position);
        var accelerationValue = acceleration ?? 0.0;
        if (accelerationValue != 0)
        {
            var extremumSeconds = -current.Velocity / accelerationValue;
            if (extremumSeconds > 0 && extremumSeconds < duration.TotalSeconds)
            {
                var extremum = Advance(
                    current,
                    acceleration,
                    TimeSpan.FromSeconds(extremumSeconds),
                    referenceHeight).Position;
                minimum = Math.Min(minimum, extremum);
                maximum = Math.Max(maximum, extremum);
            }
        }

        return new(end, minimum, maximum);
    }

    private static double DownwardStop(
        MotionState state,
        double? pressAcceleration,
        double referenceHeight)
    {
        if (state.Velocity >= 0 || pressAcceleration is not > 0)
            return state.Position;

        var secondsToStop = -state.Velocity / pressAcceleration.Value;
        return Advance(
            state,
            pressAcceleration,
            TimeSpan.FromSeconds(secondsToStop),
            referenceHeight).Position;
    }

    private static double BoundaryMargin(
        double minimum,
        double maximum,
        ControlBounds bounds) => Math.Min(
        minimum - bounds.Lower,
        bounds.Upper - maximum);

    private static ControlProjection ReleaseWithEnvelope(
        MotionEnvelope envelope,
        double margin,
        string reason,
        double? brakingPressAcceleration) => new(
            InputAction.Release,
            reason,
            null,
            null,
            envelope.AtFeedback.Position,
            envelope.Maximum,
            envelope.AtFeedback.Position,
            envelope.Maximum,
            envelope.Minimum,
            envelope.Minimum,
            envelope.Maximum,
            Math.Max(0, -margin),
            Math.Max(0, -margin),
            margin,
            margin,
            brakingPressAcceleration);

    private static MotionState Advance(
        double position,
        double velocity,
        double? acceleration,
        TimeSpan duration,
        double referenceHeight) => Advance(
            new MotionState(position, velocity),
            acceleration,
            duration,
            referenceHeight);

    private static MotionState Advance(
        MotionState state,
        double? acceleration,
        TimeSpan duration,
        double referenceHeight)
    {
        var seconds = Math.Max(0, duration.TotalSeconds);
        var accelerationValue = acceleration ?? 0.0;
        return new(
            state.Position + referenceHeight
                * (state.Velocity * seconds
                    + 0.5 * accelerationValue * seconds * seconds),
            state.Velocity + accelerationValue * seconds);
    }

    private static double UpwardPeak(
        MotionState state,
        double? releaseAcceleration,
        double referenceHeight)
    {
        if (state.Velocity <= 0 || releaseAcceleration is not < 0)
            return state.Position;

        var secondsToStop = -state.Velocity / releaseAcceleration.Value;
        return Advance(
            state,
            releaseAcceleration,
            TimeSpan.FromSeconds(secondsToStop),
            referenceHeight).Position;
    }

    private static bool ValidDynamicsInterval(TimeSpan interval) =>
        interval >= MinimumDynamicsInterval && interval <= MaximumDynamicsInterval;

    private static bool StableHeights(
        double first,
        double second,
        double third,
        double referenceHeight)
    {
        if (referenceHeight <= 0 || first <= 0 || second <= 0 || third <= 0)
            return false;
        var minimum = Math.Min(first, Math.Min(second, third));
        var maximum = Math.Max(first, Math.Max(second, third));
        return (maximum - minimum) / referenceHeight <= MaximumHeightVariationRatio;
    }

    private static void Enqueue<T>(
        Queue<T> queue,
        T value,
        int capacity = RecentValueCapacity)
    {
        queue.Enqueue(value);
        while (queue.Count > capacity)
            queue.Dequeue();
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0) return 0.0;
        var middle = values.Length / 2;
        return values.Length % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2.0;
    }

    private static TimeSpan Percentile95(IEnumerable<TimeSpan> values)
    {
        var ticks = values.Select(value => value.Ticks).Order().ToArray();
        if (ticks.Length == 0) return TimeSpan.Zero;
        var index = Math.Max(0, (int)Math.Ceiling(ticks.Length * 0.95) - 1);
        return TimeSpan.FromTicks(ticks[index]);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;

    private static string CreateInvalidFrameDiagnostic(
        TimeSpan frameAge,
        MinigameInputState inputState) => string.Format(
            CultureInfo.InvariantCulture,
            "control=center_prediction frame_age_ms={0:F1} input_state={1} action={2} reason=control_frame_stale",
            frameAge.TotalMilliseconds,
            inputState,
            InputAction.Release);

    private string CreateDiagnostic(
        double zoneVelocity,
        double targetVelocity,
        TimeSpan frameAge,
        TimeSpan feedbackTime,
        ControlBounds bounds,
        MotionState currentZone,
        MotionState currentTarget,
        MotionState targetAtFeedback,
        MotionState current,
        ControlProjection projection,
        MinigameInputState inputState,
        TimeSpan minimumPulseDuration,
        double referenceHeight,
        MinigameInputTimeline inputTimeline) => string.Format(
            CultureInfo.InvariantCulture,
            "control=center_prediction relative_prediction=true velocity_up_px_s={0:F2} velocity_up_h_s={1:F3} "
            + "target_velocity_up_px_s={2:F2} target_velocity_up_h_s={3:F3} "
            + "relative_velocity_up_px_s={4:F2} relative_velocity_up_h_s={5:F3} "
            + "frame_age_ms={6:F1} decision_interval_p95_ms={7:F1} "
            + "range_low_relative_up={8:F2} range_high_relative_up={9:F2} "
            + "current_relative_up={10:F2} predicted_relative_up={11:F2} "
            + "release_peak_relative_up={12:F2} pulse_relative_up={13:F2} "
            + "pulse_peak_relative_up={14:F2} wait_brake_min_relative_up={15:F2} "
            + "pulse_min_relative_up={16:F2} pulse_max_relative_up={17:F2} "
            + "wait_violation_px={18:F2} pulse_violation_px={19:F2} "
            + "wait_margin_px={20:F2} pulse_margin_px={21:F2} reference_height={22:F2} "
            + "release_accel_up_h_s2={23} press_accel_up_h_s2={24} brake_press_accel_up_h_s2={25} "
            + "zone_current_up={26:F2} target_current_up={27:F2} target_feedback_up={28:F2} "
            + "input_state={29} action={30} pulse_min_ms={31:F0} predicted_release_ms={32} reason={33} "
            + "predicted_repress_ms={34} plan_horizon_ms={35:F1} feedback_timeout_ms={36:F1} "
            + "wait_brake_min_up={37:F2} input_state_at_capture={38} input_transition_count={39}",
            zoneVelocity * referenceHeight,
            zoneVelocity,
            targetVelocity * referenceHeight,
            targetVelocity,
            current.Velocity * referenceHeight,
            current.Velocity,
            frameAge.TotalMilliseconds,
            feedbackTime.TotalMilliseconds,
            bounds.Lower,
            bounds.Upper,
            current.Position,
            projection.PredictedPosition,
            projection.ReleasePeak,
            projection.PulsePosition,
            projection.PulsePeak,
            projection.WaitBrakeMinimum,
            projection.PulseMinimum,
            projection.PulseMaximum,
            projection.WaitViolation,
            projection.PulseViolation,
            projection.WaitMargin,
            projection.PulseMargin,
            referenceHeight,
            FormatOptional(_releaseAcceleration),
            FormatOptional(_pressAcceleration),
            FormatOptional(projection.BrakingPressAcceleration),
            currentZone.Position,
            currentTarget.Position,
            targetAtFeedback.Position,
            inputState,
            projection.Action,
            minimumPulseDuration.TotalMilliseconds,
            FormatOptionalMilliseconds(projection.PredictedReleaseDelay),
            projection.Reason,
            FormatOptionalMilliseconds(projection.PredictedRepressDelay),
            feedbackTime.TotalMilliseconds,
            FeedbackTimeout(feedbackTime).TotalMilliseconds,
            projection.WaitBrakeMinimum + currentTarget.Position,
            inputTimeline.InitialState,
            inputTimeline.Transitions.Count);

    private static string FormatOptional(double? value) =>
        value?.ToString("F3", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatOptionalMilliseconds(TimeSpan? value) =>
        value?.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) ?? "-";

    private readonly record struct MotionFrame(
        long FrameNumber,
        TimeSpan CapturedTimestamp,
        double ZoneCenterUp,
        double TargetCenterUp,
        double ZoneHeight,
        double TargetHeight,
        MinigameInputState InputState)
    {
        public static MotionFrame From(
            DetectionObservation observation,
            BoundingBox zone,
            BoundingBox target,
            MinigameInputState inputState) => new(
                observation.FrameNumber,
                observation.CapturedTimestamp,
                -zone.CenterY,
                -target.CenterY,
                zone.Height,
                target.Height,
                inputState);
    }

    private readonly record struct MotionState(double Position, double Velocity);

    private readonly record struct ControlBounds(double Lower, double Upper)
    {
        public bool IsValid => Lower <= Upper;
        public double Midpoint => (Lower + Upper) / 2.0;

        public static ControlBounds From(MotionFrame frame)
        {
            var halfRange = (frame.ZoneHeight - frame.TargetHeight) / 2.0;
            return new(
                -halfRange,
                halfRange);
        }
    }

    private readonly record struct ControlProjection(
        InputAction Action,
        string Reason,
        TimeSpan? PredictedReleaseDelay,
        TimeSpan? PredictedRepressDelay,
        double PredictedPosition,
        double ReleasePeak,
        double PulsePosition,
        double PulsePeak,
        double WaitBrakeMinimum,
        double PulseMinimum,
        double PulseMaximum,
        double WaitViolation,
        double PulseViolation,
        double WaitMargin,
        double PulseMargin,
        double? BrakingPressAcceleration)
    {
        public static ControlProjection Release(double position, string reason) => new(
            InputAction.Release,
            reason,
            null,
            null,
            position,
            position,
            position,
            position,
            position,
            position,
            position,
            0,
            0,
            0,
            0,
            null);
    }

    private readonly record struct MotionSegment(
        MotionState End,
        double Minimum,
        double Maximum);

    private readonly record struct MotionEnvelope(
        MotionState AtFeedback,
        double Minimum,
        double Maximum);

    private readonly record struct RepressPlan(
        TimeSpan Delay,
        MotionEnvelope Envelope,
        double Margin);

}
