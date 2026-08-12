using Microsoft.Extensions.Logging;
using VrcFisher.Core;

namespace VrcFisher.Application;

public sealed class RuntimeController : IRuntimeController
{
    private readonly IModelCatalog modelCatalog;
    private readonly IDetectionRuntime detectionRuntime;
    private readonly IInputController inputController;
    private readonly ILogger<RuntimeController> logger;
    private readonly object _sync = new();
    private RuntimeSnapshot _snapshot = new(
        FishingPhase.Stopped,
        false,
        false,
        false,
        "Unavailable",
        0,
        0,
        "模型未安装，识别与自动输入已禁用",
        DateTimeOffset.UtcNow);

    public RuntimeSnapshot Snapshot
    {
        get { lock (_sync) return _snapshot; }
    }

    public event EventHandler<RuntimeSnapshot>? SnapshotChanged;

    public RuntimeController(
        IModelCatalog modelCatalog,
        IDetectionRuntime detectionRuntime,
        IInputController inputController,
        ILogger<RuntimeController> logger)
    {
        this.modelCatalog = modelCatalog;
        this.detectionRuntime = detectionRuntime;
        this.inputController = inputController;
        this.logger = logger;
        detectionRuntime.MetricsChanged += OnMetricsChanged;
    }

    public async Task StartObservationAsync(bool automatic, CancellationToken cancellationToken)
    {
        await modelCatalog.RefreshAsync(cancellationToken);
        if (!modelCatalog.IsReady || !detectionRuntime.IsReady)
        {
            Update(_snapshot with
            {
                ModelsReady = modelCatalog.IsReady,
                IsObserving = false,
                IsAutomatic = false,
                Phase = FishingPhase.Stopped,
                Provider = detectionRuntime.Provider,
                Message = "两个有效 ONNX 模型都安装后才能开始观察"
            });
            return;
        }

        Update(_snapshot with
        {
            IsObserving = true,
            IsAutomatic = automatic,
            ModelsReady = true,
            Phase = FishingPhase.Idle,
            Provider = detectionRuntime.Provider,
            Message = automatic ? "自动运行已启动" : "仅观察已启动"
        });
        try
        {
            await detectionRuntime.StartAsync(automatic, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopAsync(CancellationToken.None);
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "runtime stopped because detection failed");
            inputController.ReleaseAll();
            Update(_snapshot with
            {
                IsObserving = false,
                IsAutomatic = false,
                Phase = FishingPhase.Recovery,
                Message = $"识别已停止：{error.Message}"
            });
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await detectionRuntime.StopAsync(cancellationToken);
        }
        finally
        {
            inputController.ReleaseAll();
            Update(_snapshot with
            {
                IsObserving = false,
                IsAutomatic = false,
                Phase = FishingPhase.Stopped,
                Message = "已停止并释放鼠标"
            });
        }
    }

    public void UpdateMetrics(long captured, long dropped, FishingPhase phase, string message)
    {
        Update(_snapshot with
        {
            FramesCaptured = captured,
            FramesDropped = dropped,
            Phase = phase,
            Message = message,
            Provider = detectionRuntime.Provider
        });
    }

    private void Update(RuntimeSnapshot snapshot)
    {
        RuntimeSnapshot updated;
        lock (_sync) _snapshot = updated = snapshot with { UpdatedAt = DateTimeOffset.UtcNow };
        SnapshotChanged?.Invoke(this, updated);
    }

    private void OnMetricsChanged(object? sender, DetectionRuntimeMetrics metrics)
    {
        Update(_snapshot with
        {
            FramesCaptured = metrics.FramesCaptured,
            FramesDropped = metrics.FramesDropped,
            Phase = metrics.Phase,
            Message = metrics.Message,
            Provider = detectionRuntime.Provider
        });
    }
}
