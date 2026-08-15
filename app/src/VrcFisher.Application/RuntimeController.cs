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
        InferencePerformanceSnapshot.Default,
        new RuntimeStatus(RuntimeMessageCode.ModelsUnavailable),
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!inputController.IsTargetForeground)
        {
            Update(_snapshot with
            {
                IsObserving = false,
                IsAutomatic = false,
                Phase = FishingPhase.Stopped,
                Status = new RuntimeStatus(RuntimeMessageCode.StartTargetNotForeground)
            });
            return;
        }

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
                Status = new RuntimeStatus(RuntimeMessageCode.ModelsRequired)
            });
            return;
        }
        if (!modelCatalog.AutomaticAllowed)
        {
            Update(_snapshot with
            {
                ModelsReady = true,
                IsObserving = false,
                IsAutomatic = false,
                Phase = FishingPhase.Stopped,
                Provider = detectionRuntime.Provider,
                Status = new RuntimeStatus(RuntimeMessageCode.AutomaticNotAllowed)
            });
            return;
        }
        if (!inputController.IsTargetForeground)
        {
            Update(_snapshot with
            {
                IsObserving = false,
                IsAutomatic = false,
                Phase = FishingPhase.Stopped,
                Status = new RuntimeStatus(RuntimeMessageCode.StartTargetNotForeground)
            });
            return;
        }

        Update(_snapshot with
        {
            IsObserving = true,
            IsAutomatic = true,
            ModelsReady = true,
            Phase = FishingPhase.Idle,
            Provider = detectionRuntime.Provider,
            Status = new RuntimeStatus(RuntimeMessageCode.AutomaticStarted)
        });
        try
        {
            await detectionRuntime.StartAsync(cancellationToken);
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
                Status = new RuntimeStatus(RuntimeMessageCode.DetectionStopped, error.Message)
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
                Status = new RuntimeStatus(RuntimeMessageCode.Stopped)
            });
        }
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
            Performance = metrics.Performance,
            Status = metrics.Status,
            Provider = detectionRuntime.Provider
        });
    }
}
