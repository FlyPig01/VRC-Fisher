using Microsoft.Extensions.Logging;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Infrastructure.Capture;
using VrcFisher.Infrastructure.Inference;

namespace VrcFisher.Infrastructure.Runtime;

public sealed class DetectionRuntime(
    DirectoryLayout layout,
    AppOptions options,
    WindowsGraphicsCaptureSource capture,
    IModelCatalog modelCatalog,
    IInputController inputController,
    ILogger<DetectionRuntime> logger) : IDetectionRuntime, IAsyncDisposable
{
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
        lock (_sync)
        {
            if (_runTask is not null) return;
            _detector = new OnnxRuntimeDetector(layout.Models, options.Device);
            if (automatic && !_detector.CanProduceDecisions)
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
            try { await task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
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
            var frame = await _frames.WaitAsync(cancellationToken);
            if (frame is null) continue;
            var detector = _detector;
            if (detector is null) continue;
            try
            {
                var observation = detector.Detect(frame);
                if (!detector.CanProduceDecisions)
                {
                    Publish(FishingPhase.Stopped, "模型已加载，但输出契约未验证；仅可检查 Provider");
                    continue;
                }
                var decision = _stateMachine.Step(observation, DateTimeOffset.UtcNow);
                if (_automatic) Apply(decision.Action);
                Publish(decision.Phase, decision.Reason);
            }
            catch (Exception error)
            {
                logger.LogError(error, "frame inference failed; input released");
                inputController.ReleaseAll();
                Publish(FishingPhase.Recovery, $"识别失败：{error.Message}");
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

    private void Publish(FishingPhase phase, string message)
    {
        MetricsChanged?.Invoke(this, new DetectionRuntimeMetrics(
            Interlocked.Read(ref _captured),
            _frames.DroppedCount,
            phase,
            message,
            DateTimeOffset.UtcNow));
    }
}
