using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Infrastructure.Capture;
using VrcFisher.Infrastructure.Inference;

namespace VrcFisher.Infrastructure.Runtime;

public sealed class DetectionRuntime(
    DirectoryLayout layout,
    Func<AppOptions> optionsProvider,
    IFrameSource capture,
    IModelCatalog modelCatalog,
    IInputController inputController,
    ILogger<DetectionRuntime> logger) : IDetectionRuntime, IAsyncDisposable
{
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FrameWaitTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan MaximumFrameAge = TimeSpan.FromMilliseconds(750);
    private readonly object _sync = new();
    private readonly LatestFrameBuffer _frames = new();
    private readonly PerformanceProfileStore _profileStore = new(layout.Config);
    private FishingStateMachine _stateMachine = new(StateMachineOptions.Default);
    private InferencePerformanceScheduler? _performance;
    private PerformanceProfileIdentity? _profileIdentity;
    private OnnxRuntimeDetector? _detector;
    private CancellationTokenSource? _runCancellation;
    private TaskCompletionSource<bool>? _firstFrame;
    private Task? _runTask;
    private ExecutionRuntimeInfo _execution = ExecutionRuntimeInfo.Unavailable();
    private bool _prepared;
    private long _captured;
    private long _lastDroppedForSample;
    private DateTimeOffset _lastInferenceAt = DateTimeOffset.MinValue;

    public ExecutionRuntimeInfo Execution
    {
        get { lock (_sync) return _execution; }
    }

    public bool IsReady => modelCatalog.IsReady;
    public event EventHandler<DetectionRuntimeMetrics>? MetricsChanged;
    public event EventHandler<DetectionVisualizationFrame>? VisualizationChanged;

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (!modelCatalog.IsReady)
            throw new InvalidOperationException("模型未安装或未通过校验");

        lock (_sync)
        {
            if (_prepared) return;
        }

        var options = optionsProvider();
        var detector = new OnnxRuntimeDetector(
            layout.Models,
            options.Device,
            (float)options.ConfidenceThreshold,
            (float)options.IoUThreshold);
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
            _stateMachine = new FishingStateMachine(StateMachineOptions.Default with
            {
                BiteFallback = options.BiteFallbackEnabled
                    ? TimeSpan.FromSeconds(options.BiteFallbackSeconds)
                    : TimeSpan.Zero
            });
            _stateMachine.Reset(DateTimeOffset.UtcNow);
            _detector = detector;
            _execution = detector.Execution;
            _performance = new InferencePerformanceScheduler(options, detector.Execution.ProfileKey);
            _profileIdentity = null;
            _captured = 0;
            _lastDroppedForSample = 0;
            _lastInferenceAt = DateTimeOffset.MinValue;
            _runCancellation = runCancellation;
            _firstFrame = firstFrame;
            _prepared = true;
            capture.FrameArrived += OnFrameArrived;
        }

        try
        {
            await capture.StartAsync(runCancellation.Token);
            using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                runCancellation.Token);
            await firstFrame.Task.WaitAsync(FirstFrameTimeout, startupCancellation.Token);
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
            "automatic detection runtime prepared backend={Backend} requested={Requested} fallback={Fallback}",
            detector.Execution.Backend,
            detector.Execution.Requested,
            detector.Execution.FellBack);
    }

    public void Activate()
    {
        lock (_sync)
        {
            if (!_prepared || _runCancellation is null)
                throw new InvalidOperationException("检测运行时尚未完成准备");
            if (_runTask is not null) return;
            var runToken = _runCancellation.Token;
            _runTask = Task.Run(() => ProcessFramesAsync(runToken), runToken);
        }
        logger.LogInformation("automatic detection runtime activated");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? task;
        CancellationTokenSource? runCancellation;
        OnnxRuntimeDetector? detector;
        InferencePerformanceScheduler? performance;
        PerformanceProfileIdentity? profileIdentity;
        lock (_sync)
        {
            runCancellation = _runCancellation;
            runCancellation?.Cancel();
            task = _runTask;
            _runTask = null;
            capture.FrameArrived -= OnFrameArrived;
            detector = _detector;
            _detector = null;
            performance = _performance;
            profileIdentity = _profileIdentity;
            _performance = null;
            _profileIdentity = null;
            _firstFrame = null;
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

        detector?.Dispose();
        runCancellation?.Dispose();
        lock (_sync)
        {
            if (ReferenceEquals(_runCancellation, runCancellation)) _runCancellation = null;
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

    private async Task ProcessFramesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await _frames.WaitAsync(FrameWaitTimeout, cancellationToken);
            if (frame is null)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    inputController.ReleaseAll();
                    _stateMachine.Reset(DateTimeOffset.UtcNow);
                    Publish(FishingPhase.Recovery, RuntimeMessageCode.CaptureStopped);
                }
                continue;
            }
            var detector = _detector;
            var performance = _performance;
            if (detector is null || performance is null) continue;
            try
            {
                if (DateTimeOffset.UtcNow - frame.CapturedAt > MaximumFrameAge)
                {
                    inputController.ReleaseAll();
                    _stateMachine.Reset(DateTimeOffset.UtcNow);
                    Publish(FishingPhase.Recovery, RuntimeMessageCode.FrameStale);
                    continue;
                }
                if (!inputController.IsTargetForeground)
                {
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
                if (_lastInferenceAt != DateTimeOffset.MinValue
                    && frame.CapturedAt - _lastInferenceAt < interval)
                {
                    continue;
                }
                _lastInferenceAt = frame.CapturedAt;
                var frameAge = DateTimeOffset.UtcNow - frame.CapturedAt;
                var currentOptions = optionsProvider();
                var inferenceStarted = Stopwatch.GetTimestamp();
                var detection = detector.Detect(
                    frame,
                    phase,
                    performance.PanelRecheckInterval,
                    currentOptions.WorkMode == ApplicationMode.Debug);
                var inferenceElapsed = Stopwatch.GetElapsedTime(inferenceStarted);
                var dropped = _frames.DroppedCount;
                performance.Record(
                    detection.Workload,
                    inferenceElapsed.TotalMilliseconds,
                    frameAge.TotalMilliseconds,
                    Math.Max(0, dropped - _lastDroppedForSample),
                    DateTimeOffset.UtcNow);
                _lastDroppedForSample = dropped;
                if (!detector.CanProduceDecisions)
                {
                    Publish(FishingPhase.Stopped, RuntimeMessageCode.OutputContractUnverified);
                    continue;
                }
                if (detection.Visualization is not null)
                    PublishVisualization(detection.Visualization);
                _stateMachine.UpdateBiteFallback(currentOptions.BiteFallbackEnabled
                    ? TimeSpan.FromSeconds(currentOptions.BiteFallbackSeconds)
                    : TimeSpan.Zero);
                var decision = _stateMachine.Step(detection.Observation, DateTimeOffset.UtcNow);
                Apply(decision.Action);
                Publish(decision.Phase, RuntimeMessageCode.StateMachineDecision, decision.Reason);
            }
            catch (Exception error)
            {
                performance.RecordFailure(DateTimeOffset.UtcNow);
                logger.LogError(error, "frame inference failed; input released");
                inputController.ReleaseAll();
                Publish(FishingPhase.Recovery, RuntimeMessageCode.InferenceFailed, error.Message);
            }
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

    private void Apply(InputAction action)
    {
        switch (action)
        {
            case InputAction.Click: inputController.Click(); break;
            case InputAction.Press: inputController.PressLeft(); break;
            case InputAction.Release: inputController.ReleaseLeft(); break;
        }
    }

    private void Publish(FishingPhase phase, RuntimeMessageCode code, string? detail = null)
    {
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
}
