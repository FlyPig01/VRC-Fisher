using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Infrastructure.Capture;
using VrcFisher.Infrastructure.Inference;
using VrcFisher.Infrastructure.Input;

namespace VrcFisher.Infrastructure.Runtime;

public sealed class DetectionRuntime(
    DirectoryLayout layout,
    Func<AppOptions> optionsProvider,
    IFrameSource capture,
    IModelCatalog modelCatalog,
    IInputController inputController,
    ILogger<DetectionRuntime> logger,
    Func<AppOptions, IDetector>? detectorFactory = null,
    Func<TimeSpan, CancellationToken, Task>? pulseDelay = null) : IDetectionRuntime, IAsyncDisposable
{
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FrameWaitTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan MaximumFrameAge = TimeSpan.FromMilliseconds(750);
    private static TimeSpan MonotonicNow =>
        Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());
    private readonly object _sync = new();
    private readonly LatestFrameBuffer _frames = new();
    private readonly PerformanceProfileStore _profileStore = new(layout.Config);
    private readonly MinigameDynamicsStore _dynamicsStore = new(layout.Config);
    private readonly MinigamePulseExecutor _pulseExecutor = new(inputController, pulseDelay);
    private FishingStateMachine _stateMachine = new(StateMachineOptions.Default);
    private InferencePerformanceScheduler? _performance;
    private PerformanceProfileIdentity? _profileIdentity;
    private IDetector? _detector;
    private CancellationTokenSource? _runCancellation;
    private TaskCompletionSource<bool>? _firstFrame;
    private Task? _runTask;
    private ExecutionRuntimeInfo _execution = ExecutionRuntimeInfo.Unavailable();
    private bool _prepared;
    private Exception? _captureFailure;
    private long _captured;
    private long _lastDroppedForSample;
    private TimeSpan _lastInferenceTimestamp;
    private TimeSpan _acceptFramesAfterTimestamp;
    private string _sessionId = "-";
    private DateTimeOffset _sessionStartedAt = DateTimeOffset.MinValue;
    private long _decisionId;
    private long _operationId;
    private int _lastCycle;
    private FishingPhase _lastLoggedPhase = FishingPhase.Stopped;
    private bool _hasLoggedDetectionState;
    private bool _loggedPanelPresent;
    private bool _loggedComponentsComplete;
    private long _loggedPanelGeneration;
    private InputAction _lastMinigameControlAction = InputAction.Release;
    private PendingMinigameEnd? _pendingMinigameEnd;
    private PostCastObservation? _postCastObservation;

    public ExecutionRuntimeInfo Execution
    {
        get { lock (_sync) return _execution; }
    }

    public bool IsReady => modelCatalog.IsReady;
    public event EventHandler<DetectionRuntimeMetrics>? MetricsChanged;
    public event EventHandler<DetectionVisualizationFrame>? VisualizationChanged;
    public event EventHandler<FishingOperationTrace>? FishingOperationSubmitted;

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (!modelCatalog.IsReady)
            throw new InvalidOperationException("模型未安装或未通过校验");

        lock (_sync)
        {
            if (_prepared) return;
        }

        var options = optionsProvider();
        var initialDynamics = _dynamicsStore.Load();
        var detector = detectorFactory?.Invoke(options) ?? new OnnxRuntimeDetector(
            layout.Models,
            options.Device,
            (float)options.ConfidenceThreshold,
            (float)options.IoUThreshold,
            (float)options.BiteIndicatorConfidenceThreshold);
        if (!modelCatalog.AutomaticAllowed || !detector.CanProduceDecisions)
        {
            detector.Dispose();
            throw new InvalidOperationException("当前 ONNX 输出契约尚未验证，自动输入已禁用");
        }

        var runCancellation = new CancellationTokenSource();
        var firstFrame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_prepared)
            {
                detector.Dispose();
                runCancellation.Dispose();
                return;
            }

            _frames.Clear();
            _stateMachine = new FishingStateMachine(
                StateMachineOptions.Default with
                {
                    BiteFallback = options.BiteFallbackEnabled
                        ? TimeSpan.FromSeconds(options.BiteFallbackSeconds)
                        : TimeSpan.Zero
                },
                initialDynamics);
            _stateMachine.Reset(DateTimeOffset.UtcNow);
            _detector = detector;
            _execution = detector.Execution;
            _performance = new InferencePerformanceScheduler(options, detector.Execution.ProfileKey);
            _profileIdentity = null;
            _captured = 0;
            _lastDroppedForSample = 0;
            _lastInferenceTimestamp = default;
            _acceptFramesAfterTimestamp = default;
            _sessionId = Guid.NewGuid().ToString("N");
            _sessionStartedAt = DateTimeOffset.UtcNow;
            _decisionId = 0;
            _operationId = 0;
            _lastCycle = 0;
            _lastLoggedPhase = FishingPhase.Idle;
            _hasLoggedDetectionState = false;
            _loggedPanelPresent = false;
            _loggedComponentsComplete = false;
            _loggedPanelGeneration = 0;
            _lastMinigameControlAction = InputAction.Release;
            _pendingMinigameEnd = null;
            _postCastObservation = null;
            _runCancellation = runCancellation;
            _firstFrame = firstFrame;
            _captureFailure = null;
            _prepared = true;
            capture.FrameArrived += OnFrameArrived;
            capture.CaptureFailed += OnCaptureFailed;
        }

        try
        {
            RequestNextFrame(TimeSpan.Zero);
            await capture.StartAsync(runCancellation.Token);
            using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                runCancellation.Token);
            await firstFrame.Task.WaitAsync(FirstFrameTimeout, startupCancellation.Token);
            lock (_sync)
            {
                if (_captureFailure is not null)
                    throw new InvalidOperationException("屏幕捕获在首帧准备期间失败", _captureFailure);
            }
        }
        catch (TimeoutException error)
        {
            await StopAsync(CancellationToken.None);
            throw new TimeoutException("屏幕捕获已启动，但 5 秒内没有收到有效画面", error);
        }
        catch
        {
            await StopAsync(CancellationToken.None);
            throw;
        }

        logger.LogInformation(
            "runtime session_started session={Session} backend={Backend} requested={Requested} fallback={Fallback} model_version={ModelVersion}",
            _sessionId,
            detector.Execution.Backend,
            detector.Execution.Requested,
            detector.Execution.FellBack,
            modelCatalog.InstalledVersion ?? "-");
    }

    public void Activate()
    {
        lock (_sync)
        {
            if (!_prepared || _runCancellation is null)
                throw new InvalidOperationException("检测运行时尚未完成准备");
            if (_captureFailure is not null)
                throw new InvalidOperationException("屏幕捕获在激活前失败", _captureFailure);
            if (_runTask is not null) return;
            var runToken = _runCancellation.Token;
            _runTask = Task.Run(() => ProcessFramesAsync(runToken), runToken);
        }
        logger.LogInformation("runtime activated session={Session}", _sessionId);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        inputController.ReleaseAll();
        Task? task;
        CancellationTokenSource? runCancellation;
        IDetector? detector;
        InferencePerformanceScheduler? performance;
        PerformanceProfileIdentity? profileIdentity;
        var wasPrepared = false;
        var stoppedSession = "-";
        var stoppedAt = DateTimeOffset.UtcNow;
        var finalPhase = FishingPhase.Stopped;
        lock (_sync)
        {
            wasPrepared = _prepared;
            stoppedSession = _sessionId;
            stoppedAt = DateTimeOffset.UtcNow;
            finalPhase = _stateMachine.Phase;
            runCancellation = _runCancellation;
            runCancellation?.Cancel();
            task = _runTask;
            _runTask = null;
            capture.FrameArrived -= OnFrameArrived;
            capture.CaptureFailed -= OnCaptureFailed;
            detector = _detector;
            _detector = null;
            performance = _performance;
            profileIdentity = _profileIdentity;
            _performance = null;
            _profileIdentity = null;
            _firstFrame = null;
            _captureFailure = null;
            _prepared = false;
            _execution = ExecutionRuntimeInfo.Unavailable(optionsProvider().Device);
            _frames.Clear();
        }

        Exception? captureFailure = null;
        try
        {
            await capture.StopAsync(cancellationToken);
        }
        catch (Exception error)
        {
            captureFailure = error;
        }

        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }

        if (performance is { Adaptive: true } && profileIdentity is not null)
        {
            try
            {
                await _profileStore.SaveAsync(
                    performance.CreateProfile(profileIdentity),
                    cancellationToken);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(error, "performance profile could not be saved");
            }
        }

        try
        {
            await _dynamicsStore.SaveAsync(
                _stateMachine.MinigameDynamics,
                CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "minigame dynamics could not be saved");
        }

        detector?.Dispose();
        runCancellation?.Dispose();
        lock (_sync)
        {
            if (ReferenceEquals(_runCancellation, runCancellation)) _runCancellation = null;
        }

        if (wasPrepared)
        {
            logger.LogInformation(
                "runtime session_stopped session={Session} phase={Phase} duration_ms={Duration:F0}",
                stoppedSession,
                finalPhase,
                Math.Max(0, (stoppedAt - _sessionStartedAt).TotalMilliseconds));
        }

        if (captureFailure is not null)
            throw captureFailure;
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

    private void OnFrameArrived(object? sender, CapturedFrameEventArgs frame)
    {
        Interlocked.Increment(ref _captured);
        _frames.Publish(frame);
        if (frame.Width > 0 && frame.Height > 0 && !frame.BgraPixels.IsEmpty)
        {
            TaskCompletionSource<bool>? firstFrame;
            lock (_sync) firstFrame = _firstFrame;
            firstFrame?.TrySetResult(true);
        }
    }

    private void OnCaptureFailed(object? sender, FrameSourceFailedEventArgs args)
    {
        TaskCompletionSource<bool>? firstFrame;
        var active = false;
        lock (_sync)
        {
            if (!_prepared) return;
            _captureFailure = args.Exception;
            firstFrame = _firstFrame;
            active = _runTask is not null;
        }

        var failure = new InvalidOperationException(
            $"屏幕捕获失败：{args.Exception.GetBaseException().Message}",
            args.Exception);
        LogRecoveryEvent(
            "capture_interrupted",
            detail: args.Exception.GetBaseException().Message);
        logger.LogError(args.Exception, "screen capture callback failed; input released");
        inputController.ReleaseAll();
        if (firstFrame?.TrySetException(failure) != true && active)
            Publish(FishingPhase.Recovery, RuntimeMessageCode.DetectionStopped, failure.Message);
    }

    private async Task ProcessFramesAsync(CancellationToken cancellationToken)
    {
        PendingPulse? pendingPulse = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await _frames.WaitAsync(FrameWaitTimeout, cancellationToken);
                if (frame is null)
                {
                    if (pendingPulse is not null)
                    {
                        var completion = await CompletePulseAsync(pendingPulse);
                        pendingPulse = null;
                        if (!completion.Succeeded) return;
                        _acceptFramesAfterTimestamp = GetReleaseBoundary(
                            completion.Result.ReleasedTimestamp);
                        _lastInferenceTimestamp = default;
                        if (cancellationToken.IsCancellationRequested) return;
                        RequestNextFrame(TimeSpan.Zero);
                        continue;
                    }
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        LogRecoveryEvent("capture_wait_timeout");
                        inputController.ReleaseAll();
                        _stateMachine.Reset(DateTimeOffset.UtcNow);
                        Publish(FishingPhase.Recovery, RuntimeMessageCode.CaptureStopped);
                        RequestNextFrame(TimeSpan.Zero);
                    }
                    continue;
                }
                var detector = _detector;
                var performance = _performance;
                if (detector is null || performance is null) continue;
                var nextFrameDelay = TimeSpan.FromMilliseconds(100);
                var requestAnotherFrame = true;
                var processingStarted = Stopwatch.GetTimestamp();
                try
                {
                var controlTimestamp = MonotonicNow;
                var frameAge = controlTimestamp - frame.CapturedTimestamp;
                if (frameAge < TimeSpan.Zero || frameAge > MaximumFrameAge)
                {
                    LogRecoveryEvent(
                        "frame_stale",
                        frame.FrameNumber,
                        frame.CapturedAt,
                        $"frame_age_ms={frameAge.TotalMilliseconds:F1}");
                    pendingPulse?.RequestRelease();
                    inputController.ReleaseAll();
                    _stateMachine.Reset(DateTimeOffset.UtcNow);
                    Publish(FishingPhase.Recovery, RuntimeMessageCode.FrameStale);
                    continue;
                }
                if (frame.CapturedTimestamp <= _acceptFramesAfterTimestamp)
                {
                    nextFrameDelay = TimeSpan.Zero;
                    continue;
                }
                if (!inputController.IsTargetForeground)
                {
                    LogRecoveryEvent(
                        "target_not_foreground",
                        frame.FrameNumber,
                        frame.CapturedAt);
                    pendingPulse?.RequestRelease();
                    inputController.ReleaseAll();
                    _stateMachine.Reset(DateTimeOffset.UtcNow);
                    Publish(FishingPhase.Recovery, RuntimeMessageCode.TargetNotForeground);
                    continue;
                }
                EnsurePerformanceProfile(frame, performance);
                var phase = _stateMachine.Phase;
                var interval = phase == FishingPhase.Minigame && !detector.HasCachedPanel
                    ? performance.GetInferenceInterval(FishingPhase.WaitingForBite)
                    : performance.GetInferenceInterval(phase);
                nextFrameDelay = interval;
                if (_lastInferenceTimestamp != default
                    && frame.CapturedTimestamp - _lastInferenceTimestamp < interval)
                {
                    continue;
                }
                _lastInferenceTimestamp = frame.CapturedTimestamp;
                var currentOptions = optionsProvider();
                var inferenceStarted = Stopwatch.GetTimestamp();
                var detection = detector.Detect(
                    frame,
                    phase,
                    performance.PanelRecheckInterval,
                    currentOptions.WorkMode == ApplicationMode.Debug);
                var inferenceCompletedAt = DateTimeOffset.UtcNow;
                var inferenceElapsed = Stopwatch.GetElapsedTime(inferenceStarted);
                var dropped = _frames.DroppedCount;
                var droppedForSample = Math.Max(0, dropped - _lastDroppedForSample);
                performance.Record(
                    detection.Workload,
                    inferenceElapsed.TotalMilliseconds,
                    frameAge.TotalMilliseconds,
                    droppedForSample,
                    DateTimeOffset.UtcNow);
                _lastDroppedForSample = dropped;
                if (!detector.CanProduceDecisions)
                {
                    pendingPulse?.RequestRelease();
                    inputController.ReleaseAll();
                    Publish(FishingPhase.Stopped, RuntimeMessageCode.OutputContractUnverified);
                    continue;
                }
                if (detection.Visualization is not null)
                    PublishVisualization(detection.Visualization);
                _stateMachine.UpdateBiteFallback(currentOptions.BiteFallbackEnabled
                    ? TimeSpan.FromSeconds(currentOptions.BiteFallbackSeconds)
                    : TimeSpan.Zero);
                if (pendingPulse is not null && pendingPulse.Execution.IsCompleted)
                {
                    var pulseCycle = pendingPulse.Decision.Cycle;
                    var completion = await CompletePulseAsync(pendingPulse);
                    pendingPulse = null;
                    if (!completion.Succeeded)
                    {
                        requestAnotherFrame = false;
                        return;
                    }

                    _acceptFramesAfterTimestamp = GetReleaseBoundary(
                        completion.Result.ReleasedTimestamp);
                    _lastInferenceTimestamp = default;
                    if (frame.CapturedTimestamp <= _acceptFramesAfterTimestamp)
                    {
                        LogInference(
                            pulseCycle,
                            detection,
                            inferenceCompletedAt,
                            inferenceElapsed,
                            frameAge,
                            droppedForSample);
                        nextFrameDelay = TimeSpan.Zero;
                        continue;
                    }
                }

                var decisionAt = DateTimeOffset.UtcNow;
                controlTimestamp = MonotonicNow;
                var inputTimeline = pendingPulse is null
                    ? MinigameInputTimeline.Constant(MinigameInputState.Released)
                    : pendingPulse.InputTimeline(
                        detection.Observation.CapturedTimestamp,
                        controlTimestamp);
                var minigameInputState = inputTimeline.FinalState;
                var remainingMinimumHold = pendingPulse is null
                    ? TimeSpan.Zero
                    : pendingPulse.RemainingMinimumHold;
                var decision = _stateMachine.Step(
                    detection.Observation,
                    decisionAt,
                    minigameInputState,
                    controlTimestamp,
                    remainingMinimumHold,
                    interval,
                    inputTimeline);
                var decisionId = Interlocked.Increment(ref _decisionId);
                _lastCycle = decision.Cycle;
                LogInference(
                    decision.Cycle,
                    detection,
                    inferenceCompletedAt,
                    inferenceElapsed,
                    frameAge,
                    droppedForSample);
                LogPostCastVisualResults(detection.Observation, inferenceCompletedAt);
                LogDecision(decisionId, decision, detection.Observation);
                var operation = BeginFishingOperation(
                    decisionId,
                    decision,
                    detection.Observation,
                    decisionAt);
                ObserveMinigameDecision(
                    phase,
                    decision,
                    detection.Observation,
                    decisionAt,
                    pendingPulse is not null);
                if (decision.Diagnostic is not null && logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(
                        "control_diagnostic session={Session} cycle={Cycle} decision_id={DecisionId} source_frame={Frame} detail={Diagnostic}",
                        _sessionId,
                        decision.Cycle,
                        decisionId,
                        detection.Observation.FrameNumber,
                        decision.Diagnostic);
                }
                if (pendingPulse is not null)
                {
                    if (decision.Phase != FishingPhase.Minigame
                        || decision.Action == InputAction.Release
                        && decision.PredictedRepressDelay is null)
                    {
                        pendingPulse.RequestRelease();
                    }
                    else if (decision.HasFreshControlFeedback)
                    {
                        pendingPulse.UpdatePlan(decision);
                    }
                }
                else if (decision.Action == InputAction.Pulse)
                {
                    pendingPulse = StartPulse(
                        decisionId,
                        decision,
                        cancellationToken);
                }
                else
                {
                    var foregroundBefore = inputController.IsTargetForeground;
                    var inputStartedAt = DateTimeOffset.UtcNow;
                    var inputResult = Apply(decision.Action);
                    var inputFinishedAt = DateTimeOffset.UtcNow;
                    LogInput(
                        decisionId,
                        decision,
                        inputResult,
                        inputStartedAt,
                        inputFinishedAt,
                        null,
                        foregroundBefore,
                        inputController.IsTargetForeground,
                        operation?.OperationId);
                    CompleteFishingOperation(
                        operation,
                        decisionId,
                        inputResult,
                        inputStartedAt,
                        inputFinishedAt);
                    if (_pendingMinigameEnd is not null
                        && decision.Reason == "minigame ended")
                    {
                        LogMinigameControlEnded(inputResult, inputFinishedAt);
                    }
                    if (inputResult.Succeeded
                        && _stateMachine.AcknowledgeInputCompleted(
                            decision,
                            inputFinishedAt) is { } nextCastNotBefore)
                    {
                        logger.LogInformation(
                            "post_reel_wait_started session={Session} cycle={Cycle} reel_completed_at={ReelCompletedAt:O} delay_ms={Delay:F0} next_cast_not_before={NextCastNotBefore:O}",
                            _sessionId,
                            decision.Cycle,
                            inputFinishedAt,
                            (nextCastNotBefore - inputFinishedAt).TotalMilliseconds,
                            nextCastNotBefore);
                    }
                    LogPostCastTimeout(decision, detection.Observation, decisionAt);
                    if (!HandleInputResult(decision, inputResult, decisionAt))
                    {
                        requestAnotherFrame = false;
                        return;
                    }
                }
                Publish(decision.Phase, RuntimeMessageCode.StateMachineDecision, decision.Reason);
                var nextPhase = decision.Phase;
                var nextInterval = nextPhase == FishingPhase.Minigame && !detector.HasCachedPanel
                    ? performance.GetInferenceInterval(FishingPhase.WaitingForBite)
                    : performance.GetInferenceInterval(nextPhase);
                var elapsed = Stopwatch.GetElapsedTime(processingStarted);
                nextFrameDelay = elapsed >= nextInterval ? TimeSpan.Zero : nextInterval - elapsed;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    requestAnotherFrame = false;
                    return;
                }
                catch (Exception error)
                {
                    performance.RecordFailure(DateTimeOffset.UtcNow);
                    LogRecoveryEvent("inference_failed", detail: error.Message);
                    logger.LogError(error, "frame inference failed; input released");
                    pendingPulse?.RequestRelease();
                    inputController.ReleaseAll();
                    Publish(FishingPhase.Recovery, RuntimeMessageCode.InferenceFailed, error.Message);
                }
                finally
                {
                    if (requestAnotherFrame && !cancellationToken.IsCancellationRequested)
                        RequestNextFrame(nextFrameDelay);
                }
            }
        }
        finally
        {
            LogRecoveryEvent(
                "runtime_loop_exited",
                detail: cancellationToken.IsCancellationRequested ? "cancellation_requested" : "loop_completed");
        }
    }

    private void RequestNextFrame(TimeSpan delay)
    {
        if (capture is not IDemandDrivenFrameSource demandCapture) return;
        try
        {
            demandCapture.RequestNextFrame(delay);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "failed to schedule next capture frame");
        }
    }

    private void EnsurePerformanceProfile(
        CapturedFrameEventArgs frame,
        InferencePerformanceScheduler performance)
    {
        if (!performance.Adaptive || _profileIdentity is not null) return;
        var execution = Execution;
        var identity = PerformanceProfileStore.CreateIdentity(
            execution.ProfileKey,
            modelCatalog,
            frame.Width,
            frame.Height);
        var profile = _profileStore.Load(identity);
        if (profile is not null)
        {
            performance.ApplyProfile(profile);
            logger.LogInformation(
                "loaded inference performance profile backend={Backend} resolution={Width}x{Height}",
                execution.Backend,
                frame.Width,
                frame.Height);
        }
        _profileIdentity = identity;
    }

    private InputExecutionResult Apply(InputAction action)
    {
        return action switch
        {
            InputAction.Click => inputController.Click(),
            InputAction.Press => inputController.PressLeft(),
            InputAction.Release => inputController.ReleaseLeft(),
            _ => InputExecutionResult.NoChange
        };
    }

    private async Task<PulseExecutionResult> ApplyPulseAsync(
        StateDecision decision,
        PulseReleaseControl releaseControl,
        CancellationToken cancellationToken)
    {
        return await _pulseExecutor.ExecuteAsync(
            decision.MinimumPulseDuration,
            releaseControl,
            cancellationToken);
    }

    private PendingPulse StartPulse(
        long decisionId,
        StateDecision decision,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var startedTimestamp = MonotonicNow;
        var releaseControl = new PulseReleaseControl(
            decision.PredictedReleaseDelay,
            decision.PredictedRepressDelay,
            decision.ControlPlanHorizon > TimeSpan.Zero
                ? decision.ControlPlanHorizon
                : null,
            decision.FeedbackTimeout > TimeSpan.Zero
                ? decision.FeedbackTimeout
                : MinigamePulseExecutor.FeedbackWatchdog);
        return new PendingPulse(
            decisionId,
            decision,
            startedAt,
            startedTimestamp,
            inputController.IsTargetForeground,
            releaseControl,
            ApplyPulseAsync(decision, releaseControl, cancellationToken));
    }

    private static TimeSpan GetReleaseBoundary(TimeSpan releasedTimestamp) =>
        releasedTimestamp == default ? MonotonicNow : releasedTimestamp;

    private async Task<PulseCompletion> CompletePulseAsync(PendingPulse pending)
    {
        var result = await pending.Execution;
        var finishedAt = result.ReleasedAt == default
            ? DateTimeOffset.UtcNow
            : result.ReleasedAt;
        LogInput(
            pending.DecisionId,
            pending.Decision,
            result.Input,
            pending.StartedAt,
            finishedAt,
            result,
            pending.ForegroundBefore,
            inputController.IsTargetForeground,
            operationId: null);
        if (_pendingMinigameEnd is not null)
            LogMinigameControlEnded(result.Input, finishedAt);

        if (result.EmergencyReleased)
        {
            logger.LogError(
                "pulse safety release cause={Cause} planned_ms={Planned} actual_ms={Actual:F1}",
                result.ReleaseCause,
                FormatMilliseconds(result.PlannedHold),
                result.ActualHold.TotalMilliseconds);
        }
        else if (result.TimingOverrun)
        {
            logger.LogWarning(
                "pulse timing overrun planned_ms={Planned} release_late_ms={ReleaseLate:F1} actual_ms={Actual:F1}",
                FormatMilliseconds(result.PlannedHold),
                result.ReleaseLateness?.TotalMilliseconds ?? 0,
                result.ActualHold.TotalMilliseconds);
        }

        return new PulseCompletion(
            result,
            HandleInputResult(pending.Decision, result.Input, finishedAt));
    }

    private bool HandleInputResult(
        StateDecision decision,
        InputExecutionResult result,
        DateTimeOffset now)
    {
        if (result.Succeeded) return true;

        var detail = $"Input {decision.Action} failed "
            + $"({result.SubmittedEvents}/{result.ExpectedEvents} events): {result.Error}";
        logger.LogError(
            "input action failed phase={Phase} action={Action} submitted={Submitted} expected={Expected} error={Error}",
            decision.Phase,
            decision.Action,
            result.SubmittedEvents,
            result.ExpectedEvents,
            result.Error);
        LogRecoveryEvent("input_failed", detail: detail);
        _ = inputController.ReleaseAll();
        _stateMachine.Reset(now);
        Publish(FishingPhase.Recovery, RuntimeMessageCode.InputFailed, detail);
        return false;
    }

    private void LogInference(
        int cycle,
        DetectionResult detection,
        DateTimeOffset inferenceAt,
        TimeSpan inferenceElapsed,
        TimeSpan frameAge,
        long droppedFrames)
    {
        if (!logger.IsEnabled(LogLevel.Debug)) return;
        var observation = detection.Observation;
        logger.LogDebug(
            "inference session={Session} cycle={Cycle} frame={Frame} captured_at={CapturedAt:O} inference_at={InferenceAt:O} workload={Workload} provider={Provider} model_version={ModelVersion} inference_ms={Inference:F1} frame_age_ms={Age:F1} dropped_frames={Dropped} panel_generation={PanelGeneration} bite_indicator={BiteIndicator} bite_indicator_conf={BiteIndicatorConfidence} panel={Panel} panel_conf={PanelConfidence} catch_zone={CatchZone} catch_zone_conf={CatchZoneConfidence} moving_target={MovingTarget} moving_target_conf={MovingTargetConfidence}",
            _sessionId,
            cycle,
            observation.FrameNumber,
            observation.CapturedAt,
            inferenceAt,
            detection.Workload,
            Execution.Backend,
            modelCatalog.InstalledVersion ?? "-",
            inferenceElapsed.TotalMilliseconds,
            frameAge.TotalMilliseconds,
            droppedFrames,
            observation.PanelGeneration,
            FormatBox(observation.BiteIndicator),
            FormatConfidence(observation.BiteIndicatorConfidence),
            FormatBox(observation.MinigamePanel),
            FormatConfidence(observation.MinigamePanelConfidence),
            FormatBox(observation.CatchZone),
            FormatConfidence(observation.CatchZoneConfidence),
            FormatBox(observation.MovingTarget),
            FormatConfidence(observation.MovingTargetConfidence));
        LogDetectionState(observation);
    }

    private void LogDetectionState(DetectionObservation observation)
    {
        var panelPresent = observation.MinigamePanel is not null;
        var componentsComplete = observation.CatchZone is not null && observation.MovingTarget is not null;
        if (!_hasLoggedDetectionState)
        {
            _hasLoggedDetectionState = true;
            _loggedPanelPresent = panelPresent;
            _loggedComponentsComplete = componentsComplete;
            _loggedPanelGeneration = observation.PanelGeneration;
            if (panelPresent)
            {
                logger.LogDebug(
                    "detection_state session={Session} frame={Frame} event=panel_located panel_generation={PanelGeneration}",
                    _sessionId,
                    observation.FrameNumber,
                    observation.PanelGeneration);
            }
            if (componentsComplete)
            {
                logger.LogDebug(
                    "detection_state session={Session} frame={Frame} event=components_available panel_generation={PanelGeneration}",
                    _sessionId,
                    observation.FrameNumber,
                    observation.PanelGeneration);
            }
            return;
        }

        if (observation.PanelGeneration > 0
            && _loggedPanelGeneration > 0
            && observation.PanelGeneration != _loggedPanelGeneration)
        {
            logger.LogDebug(
                "detection_state session={Session} frame={Frame} event=panel_relocated previous_generation={PreviousGeneration} panel_generation={PanelGeneration}",
                _sessionId,
                observation.FrameNumber,
                _loggedPanelGeneration,
                observation.PanelGeneration);
        }
        else if (panelPresent != _loggedPanelPresent)
        {
            logger.LogDebug(
                "detection_state session={Session} frame={Frame} event={Event} panel_generation={PanelGeneration}",
                _sessionId,
                observation.FrameNumber,
                panelPresent ? "panel_restored" : "panel_lost",
                observation.PanelGeneration);
        }

        if (componentsComplete != _loggedComponentsComplete)
        {
            logger.LogDebug(
                "detection_state session={Session} frame={Frame} event={Event} panel_generation={PanelGeneration}",
                _sessionId,
                observation.FrameNumber,
                componentsComplete ? "components_restored" : "components_lost",
                observation.PanelGeneration);
        }

        _loggedPanelPresent = panelPresent;
        _loggedComponentsComplete = componentsComplete;
        _loggedPanelGeneration = observation.PanelGeneration;
    }

    private FishingOperationContext? BeginFishingOperation(
        long decisionId,
        StateDecision decision,
        DetectionObservation observation,
        DateTimeOffset decisionAt)
    {
        var operation = decision switch
        {
            { Phase: FishingPhase.Casting, Action: InputAction.Click } => FishingOperationKind.Cast,
            { Phase: FishingPhase.Loot, Action: InputAction.Click } => FishingOperationKind.Reel,
            _ => (FishingOperationKind?)null
        };
        if (operation is null) return null;

        var context = new FishingOperationContext(
            Interlocked.Increment(ref _operationId),
            decision.Cycle,
            operation.Value);
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "fishing_operation_requested session={Session} operation_id={OperationId} cycle={Cycle} operation={Operation} decision_id={DecisionId} source_frame={Frame} captured_at={CapturedAt:O} decision_at={DecisionAt:O}",
                _sessionId,
                context.OperationId,
                context.Cycle,
                OperationName(context.Operation),
                decisionId,
                observation.FrameNumber,
                observation.CapturedAt,
                decisionAt);
        }
        return context;
    }

    private void CompleteFishingOperation(
        FishingOperationContext? operation,
        long decisionId,
        InputExecutionResult result,
        DateTimeOffset inputStartedAt,
        DateTimeOffset inputFinishedAt)
    {
        if (operation is null) return;
        var pressedAt = result.PressedAt ?? inputStartedAt;
        var releasedAt = result.ReleasedAt ?? inputFinishedAt;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "fishing_operation_input session={Session} operation_id={OperationId} cycle={Cycle} operation={Operation} decision_id={DecisionId} down_at={DownAt:O} up_at={UpAt:O} actual_hold_ms={ActualHold:F1} submitted={Submitted} expected={Expected} succeeded={Succeeded} error={Error}",
                _sessionId,
                operation.OperationId,
                operation.Cycle,
                OperationName(operation.Operation),
                decisionId,
                pressedAt,
                releasedAt,
                Math.Max(0, (releasedAt - pressedAt).TotalMilliseconds),
                result.SubmittedEvents,
                result.ExpectedEvents,
                result.Succeeded,
                result.Error ?? "-");
        }
        if (!result.Succeeded) return;

        if (operation.Operation == FishingOperationKind.Cast)
            _postCastObservation = new PostCastObservation(operation.OperationId, operation.Cycle);
        PublishFishingOperation(new FishingOperationTrace(
            operation.OperationId,
            operation.Cycle,
            operation.Operation,
            pressedAt));
    }

    private void LogPostCastVisualResults(
        DetectionObservation observation,
        DateTimeOffset observedAt)
    {
        var pending = _postCastObservation;
        if (pending is null || !logger.IsEnabled(LogLevel.Debug)) return;

        if (!pending.BiteIndicatorLogged && observation.HasBiteIndicator)
        {
            pending.BiteIndicatorLogged = true;
            logger.LogDebug(
                "post_cast_visual session={Session} operation_id={OperationId} cycle={Cycle} event=bite_indicator frame={Frame} captured_at={CapturedAt:O} observed_at={ObservedAt:O}",
                _sessionId,
                pending.OperationId,
                pending.Cycle,
                observation.FrameNumber,
                observation.CapturedAt,
                observedAt);
        }

        if (!pending.MinigameComponentsLogged
            && observation.CatchZone is not null
            && observation.MovingTarget is not null)
        {
            pending.MinigameComponentsLogged = true;
            logger.LogDebug(
                "post_cast_visual session={Session} operation_id={OperationId} cycle={Cycle} event=minigame_components frame={Frame} captured_at={CapturedAt:O} observed_at={ObservedAt:O}",
                _sessionId,
                pending.OperationId,
                pending.Cycle,
                observation.FrameNumber,
                observation.CapturedAt,
                observedAt);
        }
    }

    private void LogPostCastTimeout(
        StateDecision decision,
        DetectionObservation observation,
        DateTimeOffset decisionAt)
    {
        var pending = _postCastObservation;
        if (pending is null || decision.Reason != "bite timeout") return;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "post_cast_visual session={Session} operation_id={OperationId} cycle={Cycle} event=timeout frame={Frame} captured_at={CapturedAt:O} observed_at={ObservedAt:O}",
                _sessionId,
                pending.OperationId,
                pending.Cycle,
                observation.FrameNumber,
                observation.CapturedAt,
                decisionAt);
        }
        _postCastObservation = null;
    }

    private void ObserveMinigameDecision(
        FishingPhase phaseBeforeDecision,
        StateDecision decision,
        DetectionObservation observation,
        DateTimeOffset decisionAt,
        bool pulseActive)
    {
        if (phaseBeforeDecision != FishingPhase.Minigame
            && decision.Phase == FishingPhase.Minigame)
        {
            _lastMinigameControlAction = InputAction.Release;
            _pendingMinigameEnd = null;
        }
        else if (phaseBeforeDecision == FishingPhase.Minigame
                 && decision.Phase == FishingPhase.Minigame
                 && decision.Action != InputAction.None)
        {
            _lastMinigameControlAction = decision.Action;
        }

        if (decision.Reason != "minigame ended") return;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "minigame_ui_disappeared session={Session} cycle={Cycle} frame={Frame} captured_at={CapturedAt:O} decision_at={DecisionAt:O}",
                _sessionId,
                decision.Cycle,
                observation.FrameNumber,
                observation.CapturedAt,
                decisionAt);
        }
        _pendingMinigameEnd = new PendingMinigameEnd(
            decision.Cycle,
            observation.FrameNumber,
            pulseActive ? InputAction.Pulse : _lastMinigameControlAction);
    }

    private void LogMinigameControlEnded(
        InputExecutionResult result,
        DateTimeOffset fallbackReleasedAt)
    {
        var pending = _pendingMinigameEnd;
        if (pending is null) return;
        _pendingMinigameEnd = null;
        var releasedAt = result.ReleasedAt ?? fallbackReleasedAt;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "minigame_control_ended session={Session} cycle={Cycle} source_frame={Frame} last_control_action={LastAction} final_release_at={ReleasedAt:O} submitted={Submitted} expected={Expected} succeeded={Succeeded} error={Error}",
                _sessionId,
                pending.Cycle,
                pending.FrameNumber,
                pending.LastControlAction,
                releasedAt,
                result.SubmittedEvents,
                result.ExpectedEvents,
                result.Succeeded,
                result.Error ?? "-");
        }
        _lastMinigameControlAction = InputAction.Release;
    }

    private void LogRecoveryEvent(
        string eventName,
        long? frameNumber = null,
        DateTimeOffset? capturedAt = null,
        string? detail = null)
    {
        if (!logger.IsEnabled(LogLevel.Debug)) return;
        logger.LogDebug(
            "recovery_event session={Session} cycle={Cycle} phase={Phase} event={Event} frame={Frame} captured_at={CapturedAt} observed_at={ObservedAt:O} detail={Detail}",
            _sessionId,
            _lastCycle,
            _stateMachine.Phase,
            eventName,
            frameNumber?.ToString(CultureInfo.InvariantCulture) ?? "-",
            capturedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "-",
            DateTimeOffset.UtcNow,
            detail ?? "-");
    }

    private void PublishFishingOperation(FishingOperationTrace trace)
    {
        if (optionsProvider().WorkMode != ApplicationMode.Debug) return;
        try
        {
            FishingOperationSubmitted?.Invoke(this, trace);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "debug fishing operation subscriber failed");
        }
    }

    private static string OperationName(FishingOperationKind operation) => operation switch
    {
        FishingOperationKind.Cast => "cast",
        FishingOperationKind.Reel => "reel",
        _ => "unknown"
    };

    private void LogDecision(
        long decisionId,
        StateDecision decision,
        DetectionObservation observation)
    {
        if (!logger.IsEnabled(LogLevel.Debug)) return;
        logger.LogDebug(
            "decision session={Session} cycle={Cycle} decision_id={DecisionId} source_frame={Frame} phase={Phase} action={Action} reason={Reason} minimum_hold_ms={MinimumHold:F0} predicted_release_ms={PredictedRelease} predicted_repress_ms={PredictedRepress} plan_horizon_ms={PlanHorizon:F1} feedback_timeout_ms={FeedbackTimeout:F1} catch_zone={CatchZone} moving_target={MovingTarget}",
            _sessionId,
            decision.Cycle,
            decisionId,
            observation.FrameNumber,
            decision.Phase,
            decision.Action,
            decision.Reason,
            decision.MinimumPulseDuration.TotalMilliseconds,
            FormatMilliseconds(decision.PredictedReleaseDelay),
            FormatMilliseconds(decision.PredictedRepressDelay),
            decision.ControlPlanHorizon.TotalMilliseconds,
            decision.FeedbackTimeout.TotalMilliseconds,
            FormatBox(observation.CatchZone),
            FormatBox(observation.MovingTarget));
    }

    private void LogInput(
        long decisionId,
        StateDecision decision,
        InputExecutionResult result,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        PulseExecutionResult? pulse,
        bool foregroundBefore,
        bool foregroundAfter,
        long? operationId)
    {
        if (decision.Action == InputAction.None || !logger.IsEnabled(LogLevel.Debug)) return;
        var pressedAt = result.PressedAt ?? startedAt;
        var releasedAt = result.ReleasedAt ?? finishedAt;
        var actualHold = pulse?.ActualHold.TotalMilliseconds
            ?? Math.Max(0, (releasedAt - pressedAt).TotalMilliseconds);
        logger.LogDebug(
            "input session={Session} cycle={Cycle} decision_id={DecisionId} action={Action} operation_id={OperationId} down_at={DownAt:O} up_at={UpAt:O} minimum_hold_ms={MinimumHold:F0} predicted_release_ms={PredictedRelease} predicted_repress_ms={PredictedRepress} plan_horizon_ms={PlanHorizon:F1} final_plan_ms={FinalPlan} actual_hold_ms={ActualHold:F1} release_late_ms={ReleaseLate} release_cause={ReleaseCause} release_requested={ReleaseRequested} timing_overrun={TimingOverrun} emergency_release={EmergencyRelease} submitted={Submitted} expected={Expected} succeeded={Succeeded} error={Error} foreground_before={ForegroundBefore} foreground_after={ForegroundAfter}",
            _sessionId,
            decision.Cycle,
            decisionId,
            decision.Action,
            operationId?.ToString(CultureInfo.InvariantCulture) ?? "-",
            pressedAt,
            releasedAt,
            decision.MinimumPulseDuration.TotalMilliseconds,
            FormatMilliseconds(decision.PredictedReleaseDelay),
            FormatMilliseconds(decision.PredictedRepressDelay),
            decision.ControlPlanHorizon.TotalMilliseconds,
            FormatMilliseconds(pulse?.PlannedHold),
            actualHold,
            FormatMilliseconds(pulse?.ReleaseLateness),
            pulse?.ReleaseCause ?? "-",
            pulse?.ReleaseRequested ?? false,
            pulse?.TimingOverrun ?? false,
            pulse?.EmergencyReleased ?? false,
            result.SubmittedEvents,
            result.ExpectedEvents,
            result.Succeeded,
            result.Error ?? "-",
            foregroundBefore,
            foregroundAfter);
    }

    private static string FormatBox(BoundingBox? box) => box is null
        ? "-"
        : FormattableString.Invariant(
            $"{box.Value.Left:F1},{box.Value.Top:F1},{box.Value.Right:F1},{box.Value.Bottom:F1}");

    private static string FormatConfidence(float? value) =>
        value?.ToString("F3", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatMilliseconds(TimeSpan? value) =>
        value?.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) ?? "-";

    private void Publish(FishingPhase phase, RuntimeMessageCode code, string? detail = null)
    {
        if (phase != _lastLoggedPhase)
        {
            logger.LogInformation(
                "state_changed session={Session} phase={Phase} code={Code} detail={Detail}",
                _sessionId,
                phase,
                code,
                detail ?? "-");
            _lastLoggedPhase = phase;
        }
        MetricsChanged?.Invoke(this, new DetectionRuntimeMetrics(
            Interlocked.Read(ref _captured),
            _frames.DroppedCount,
            phase,
            _performance?.Snapshot ?? InferencePerformanceSnapshot.Default,
            new RuntimeStatus(code, detail),
            DateTimeOffset.UtcNow));
    }

    private void PublishVisualization(DetectionVisualizationFrame frame)
    {
        try
        {
            VisualizationChanged?.Invoke(this, frame);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "debug visualization subscriber failed");
        }
    }

    private sealed class PendingPulse(
        long decisionId,
        StateDecision decision,
        DateTimeOffset startedAt,
        TimeSpan startedTimestamp,
        bool foregroundBefore,
        PulseReleaseControl releaseControl,
        Task<PulseExecutionResult> execution)
    {
        public long DecisionId { get; } = decisionId;
        public StateDecision Decision { get; } = decision;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public TimeSpan StartedTimestamp { get; } = startedTimestamp;
        public bool ForegroundBefore { get; } = foregroundBefore;
        public Task<PulseExecutionResult> Execution { get; } = execution;

        public MinigameInputState InputState => releaseControl.Snapshot().IsPressed
            ? MinigameInputState.Pressed
            : MinigameInputState.Released;

        public MinigameInputTimeline InputTimeline(TimeSpan from, TimeSpan to) =>
            releaseControl.InputTimeline(from, to);

        public TimeSpan RemainingMinimumHold =>
            releaseControl.Snapshot().MinimumHoldRemaining;

        public void UpdatePlan(StateDecision next)
        {
            releaseControl.UpdatePlan(
                pressNow: next.Action == InputAction.Pulse,
                releaseDelay: next.PredictedReleaseDelay,
                repressDelay: next.PredictedRepressDelay,
                planHorizon: next.ControlPlanHorizon,
                feedbackTimeout: next.FeedbackTimeout);
        }

        public void RequestRelease() => releaseControl.RequestRelease();
    }

    private sealed record FishingOperationContext(
        long OperationId,
        int Cycle,
        FishingOperationKind Operation);

    private sealed record PendingMinigameEnd(
        int Cycle,
        long FrameNumber,
        InputAction LastControlAction);

    private sealed class PostCastObservation(long operationId, int cycle)
    {
        public long OperationId { get; } = operationId;
        public int Cycle { get; } = cycle;
        public bool BiteIndicatorLogged { get; set; }
        public bool MinigameComponentsLogged { get; set; }
    }

    private readonly record struct PulseCompletion(
        PulseExecutionResult Result,
        bool Succeeded);
}
