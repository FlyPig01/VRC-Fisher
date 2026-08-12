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
    private readonly FishingStateMachine _stateMachine = new(StateMachineOptions.Default);
    private OnnxRuntimeDetector? _detector;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private string _provider = "Unavailable";
    private bool _automatic;
    private long _captured;

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
            _stateMachine.Reset(DateTimeOffset.UtcNow);
            _detector = new OnnxRuntimeDetector(
                layout.Models,
                options.Device,
                (float)options.ConfidenceThreshold,
                (float)options.IoUThreshold,
                options.InputSize);
            if (automatic && (!modelCatalog.AutomaticAllowed || !_detector.CanProduceDecisions))
            {
                _detector.Dispose();
                _detector = null;
                throw new InvalidOperationException("当前 ONNX 输出契约尚未验证，自动输入已禁用");
            }
            _provider = _detector.Provider;
            _automatic = automatic;
            _captured = 0;
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
            if (detector is null) continue;
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
                var observation = detector.Detect(frame);
                if (!detector.CanProduceDecisions)
                {
                    Publish(FishingPhase.Stopped, RuntimeMessageCode.OutputContractUnverified);
                    continue;
                }
                var decision = _stateMachine.Step(observation, DateTimeOffset.UtcNow);
                if (_automatic) Apply(decision.Action);
                Publish(decision.Phase, RuntimeMessageCode.StateMachineDecision, decision.Reason);
            }
            catch (Exception error)
            {
                logger.LogError(error, "frame inference failed; input released");
                inputController.ReleaseAll();
                Publish(FishingPhase.Recovery, RuntimeMessageCode.InferenceFailed, error.Message);
            }
        }
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
            new RuntimeStatus(code, detail),
            DateTimeOffset.UtcNow));
    }
}
