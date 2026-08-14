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
    private Task? _runTask;
    private string _provider = "Unavailable";
    private bool _automatic;
    private long _captured;
    private long _lastDroppedForSample;
    private DateTimeOffset _lastInferenceAt = DateTimeOffset.MinValue;

    public string Provider => _provider;
    public bool IsReady => modelCatalog.IsReady;
    public event EventHandler<DetectionRuntimeMetrics>? MetricsChanged;

    public async Task StartAsync(bool automatic, CancellationToken cancellationToken)
    {
        if (!modelCatalog.IsReady)
            throw new InvalidOperationException("模型未安装或未通过校验");
        CancellationToken runToken;
        var options = optionsProvider();
        lock (_sync)
        {
            if (_runTask is not null) return;
            _stateMachine = new FishingStateMachine(StateMachineOptions.Default with
            {
                BiteFallback = TimeSpan.FromSeconds(options.BiteFallbackSeconds)
            });
            _stateMachine.Reset(DateTimeOffset.UtcNow);
            _detector = new OnnxRuntimeDetector(
                layout.Models,
                options.Device,
                (float)options.ConfidenceThreshold,
                (float)options.IoUThreshold);
            if (automatic && (!modelCatalog.AutomaticAllowed || !_detector.CanProduceDecisions))
            {
                _detector.Dispose();
                _detector = null;
                throw new InvalidOperationException("当前 ONNX 输出契约尚未验证，自动输入已禁用");
            }
            _provider = _detector.Provider;
            _performance = new InferencePerformanceScheduler(options, _provider);
            _profileIdentity = null;
            _automatic = automatic;
            _captured = 0;
            _lastDroppedForSample = _frames.DroppedCount;
            _lastInferenceAt = DateTimeOffset.MinValue;
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runToken = _runCancellation.Token;
            capture.FrameArrived += OnFrameArrived;
        }
        try
        {
            await capture.StartAsync(runToken);
            lock (_sync) _runTask = Task.Run(() => ProcessFramesAsync(runToken), runToken);
        }
        catch
        {
            capture.FrameArrived -= OnFrameArrived;
            _detector?.Dispose();
            _detector = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
            _provider = "Unavailable";
            throw;
        }
        logger.LogInformation("detection runtime started automatic={Automatic} provider={Provider}", automatic, _provider);
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
            _provider = "Unavailable";
            _automatic = false;
        }
        await capture.StopAsync(cancellationToken);
        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }
        lock (_sync)
        {
            performance = _performance;
            profileIdentity = _profileIdentity;
            _performance = null;
            _profileIdentity = null;
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
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

    private void OnFrameArrived(object? sender, CapturedFrameEventArgs frame)
    {
        Interlocked.Increment(ref _captured);
        _frames.Publish(frame);
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
                if (_automatic && !inputController.IsTargetForeground)
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
                var inferenceStarted = Stopwatch.GetTimestamp();
                var detection = detector.Detect(frame, phase, performance.PanelRecheckInterval);
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
                var decision = _stateMachine.Step(detection.Observation, DateTimeOffset.UtcNow);
                if (_automatic) Apply(decision.Action);
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
        var identity = PerformanceProfileStore.CreateIdentity(
            _provider,
            modelCatalog,
            frame.Width,
            frame.Height);
        var profile = _profileStore.Load(identity);
        if (profile is not null)
        {
            performance.ApplyProfile(profile);
            logger.LogInformation(
                "loaded inference performance profile provider={Provider} resolution={Width}x{Height}",
                _provider,
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
}
