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
    private readonly SemaphoreSlim _transition = new(1, 1);
    private CancellationTokenSource? _startupCancellation;
    private int _foregroundStopScheduled;
    private RuntimeSnapshot _snapshot = new(
        RuntimeLifecycle.Stopped,
        FishingPhase.Stopped,
        false,
        false,
        false,
        ExecutionRuntimeInfo.Unavailable(),
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
        modelCatalog.StatusChanged += OnModelStatusChanged;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _transition.WaitAsync(cancellationToken);
        CancellationTokenSource? startupCancellation = null;
        try
        {
            if (Snapshot.Lifecycle is RuntimeLifecycle.Starting or RuntimeLifecycle.Running)
                return;
            if (!inputController.IsTargetForeground)
            {
                PublishStopped(RuntimeMessageCode.StartTargetNotForeground);
                return;
            }

            startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_sync) _startupCancellation = startupCancellation;
            Update(Snapshot with
            {
                Lifecycle = RuntimeLifecycle.Starting,
                IsObserving = false,
                IsAutomatic = false,
                Phase = FishingPhase.Stopped,
                Status = new RuntimeStatus(RuntimeMessageCode.Starting)
            });

            await modelCatalog.RefreshAsync(startupCancellation.Token);
            if (!modelCatalog.IsReady || !detectionRuntime.IsReady)
            {
                PublishStopped(RuntimeMessageCode.ModelsRequired);
                return;
            }
            if (!modelCatalog.AutomaticAllowed)
            {
                PublishStopped(RuntimeMessageCode.AutomaticNotAllowed);
                return;
            }
            if (!inputController.IsTargetForeground)
            {
                PublishStopped(RuntimeMessageCode.StartTargetNotForeground);
                return;
            }

            await detectionRuntime.PrepareAsync(startupCancellation.Token);
            if (!inputController.IsTargetForeground)
            {
                await RollbackDetectionAsync();
                PublishStopped(RuntimeMessageCode.StartTargetNotForeground);
                return;
            }

            Update(Snapshot with
            {
                Lifecycle = RuntimeLifecycle.Running,
                IsObserving = true,
                IsAutomatic = true,
                ModelsReady = true,
                Phase = FishingPhase.Idle,
                Execution = detectionRuntime.Execution,
                Status = new RuntimeStatus(RuntimeMessageCode.AutomaticStarted)
            });
            detectionRuntime.Activate();
        }
        catch (OperationCanceledException) when (startupCancellation?.IsCancellationRequested == true)
        {
            await RollbackDetectionAsync();
            PublishStopped(RuntimeMessageCode.Stopped);
            if (cancellationToken.IsCancellationRequested) throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "runtime startup transaction failed");
            inputController.ReleaseAll();
            await RollbackDetectionAsync();
            Update(Snapshot with
            {
                Lifecycle = RuntimeLifecycle.Stopped,
                IsObserving = false,
                IsAutomatic = false,
                Phase = FishingPhase.Recovery,
                Execution = ExecutionRuntimeInfo.Unavailable(Snapshot.Execution.Requested),
                Status = new RuntimeStatus(RuntimeMessageCode.DetectionStopped, error.GetBaseException().Message)
            });
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_startupCancellation, startupCancellation))
                    _startupCancellation = null;
            }
            startupCancellation?.Dispose();
            _transition.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        inputController.ReleaseAll();
        CancellationTokenSource? startupCancellation;
        lock (_sync) startupCancellation = _startupCancellation;
        startupCancellation?.Cancel();

        await _transition.WaitAsync(cancellationToken);
        try
        {
            var current = Snapshot;
            if (current.Lifecycle != RuntimeLifecycle.Stopped)
            {
                Update(current with
                {
                    Lifecycle = RuntimeLifecycle.Stopping,
                    IsObserving = false,
                    IsAutomatic = false,
                    Status = new RuntimeStatus(RuntimeMessageCode.Stopping)
                });
            }

            try
            {
                await detectionRuntime.StopAsync(cancellationToken);
            }
            finally
            {
                inputController.ReleaseAll();
                PublishStopped(RuntimeMessageCode.Stopped);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _foregroundStopScheduled, 0);
            _transition.Release();
        }
    }

    private async Task RollbackDetectionAsync()
    {
        inputController.ReleaseAll();
        try
        {
            await detectionRuntime.StopAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "runtime rollback could not stop every prepared resource cleanly");
        }
    }

    private void PublishStopped(RuntimeMessageCode code)
    {
        var current = Snapshot;
        Update(current with
        {
            Lifecycle = RuntimeLifecycle.Stopped,
            IsObserving = false,
            IsAutomatic = false,
            ModelsReady = modelCatalog.IsReady,
            Phase = FishingPhase.Stopped,
            Execution = ExecutionRuntimeInfo.Unavailable(current.Execution.Requested),
            Status = new RuntimeStatus(code)
        });
    }

    private void Update(RuntimeSnapshot snapshot)
    {
        RuntimeSnapshot updated;
        lock (_sync) _snapshot = updated = snapshot with { UpdatedAt = DateTimeOffset.UtcNow };
        SnapshotChanged?.Invoke(this, updated);
    }

    private void OnModelStatusChanged(object? sender, EventArgs args)
    {
        var current = Snapshot;
        var modelsReady = modelCatalog.IsReady;
        var status = current.Status;
        if (current.Lifecycle == RuntimeLifecycle.Stopped)
        {
            if (!modelsReady)
            {
                status = new RuntimeStatus(RuntimeMessageCode.ModelsUnavailable);
            }
            else if (detectionRuntime.IsReady
                     && status.Code is RuntimeMessageCode.ModelsRequired
                         or RuntimeMessageCode.ModelsUnavailable)
            {
                status = new RuntimeStatus(RuntimeMessageCode.Stopped);
            }
        }
        Update(current with { ModelsReady = modelsReady, Status = status });
    }

    private void OnMetricsChanged(object? sender, DetectionRuntimeMetrics metrics)
    {
        if (Snapshot.Lifecycle != RuntimeLifecycle.Running) return;
        Update(Snapshot with
        {
            FramesCaptured = metrics.FramesCaptured,
            FramesDropped = metrics.FramesDropped,
            Phase = metrics.Phase,
            Performance = metrics.Performance,
            Status = metrics.Status,
            Execution = detectionRuntime.Execution
        });

        if (metrics.Status.Code == RuntimeMessageCode.TargetNotForeground
            && Interlocked.CompareExchange(ref _foregroundStopScheduled, 1, 0) == 0)
        {
            inputController.ReleaseAll();
            _ = Task.Run(async () =>
            {
                try { await StopAsync(CancellationToken.None); }
                catch (Exception error) { logger.LogError(error, "runtime could not stop after VRChat lost foreground"); }
            });
        }
    }
}
