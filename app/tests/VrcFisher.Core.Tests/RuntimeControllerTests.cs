using Microsoft.Extensions.Logging.Abstractions;
using VrcFisher.Application;
using VrcFisher.Core;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class RuntimeControllerTests
{
    [Fact]
    public async Task Start_is_rejected_before_model_or_detection_work_when_target_is_not_foreground()
    {
        var models = new StubModelCatalog();
        var detection = new StubDetectionRuntime();
        var input = new StubInputController { IsTargetForeground = false };
        var controller = Create(models, detection, input);

        await controller.StartAsync(CancellationToken.None);

        Assert.Equal(RuntimeLifecycle.Stopped, controller.Snapshot.Lifecycle);
        Assert.Equal(RuntimeMessageCode.StartTargetNotForeground, controller.Snapshot.Status.Code);
        Assert.False(controller.Snapshot.IsObserving);
        Assert.Equal(0, models.RefreshCount);
        Assert.Equal(0, detection.PrepareCount);
    }

    [Fact]
    public async Task Start_does_not_publish_running_until_preparation_has_a_valid_frame()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detection = new StubDetectionRuntime
        {
            PrepareHandler = async cancellationToken =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        };
        var controller = Create(new StubModelCatalog(), detection, new StubInputController());

        var start = controller.StartAsync(CancellationToken.None);
        await entered.Task;

        Assert.Equal(RuntimeLifecycle.Starting, controller.Snapshot.Lifecycle);
        Assert.False(controller.Snapshot.IsAutomatic);
        Assert.Equal(0, detection.ActivateCount);

        release.SetResult();
        await start;

        Assert.Equal(RuntimeLifecycle.Running, controller.Snapshot.Lifecycle);
        Assert.True(controller.Snapshot.IsAutomatic);
        Assert.Equal(1, detection.ActivateCount);
        Assert.Equal(InferenceBackend.Cpu, controller.Snapshot.Execution.Backend);
    }

    [Fact]
    public async Task Preparation_failure_rolls_back_and_never_activates_input_processing()
    {
        var detection = new StubDetectionRuntime
        {
            PrepareHandler = _ => throw new InvalidOperationException("capture failed")
        };
        var input = new StubInputController();
        var controller = Create(new StubModelCatalog(), detection, input);

        await controller.StartAsync(CancellationToken.None);

        Assert.Equal(RuntimeLifecycle.Stopped, controller.Snapshot.Lifecycle);
        Assert.Equal(RuntimeMessageCode.DetectionStopped, controller.Snapshot.Status.Code);
        Assert.Contains("capture failed", controller.Snapshot.Status.Detail);
        Assert.Equal(0, detection.ActivateCount);
        Assert.True(detection.StopCount >= 1);
        Assert.True(input.ReleaseCount >= 1);
    }

    [Fact]
    public async Task Successful_execution_is_retained_after_stop()
    {
        var controller = Create(new StubModelCatalog(), new StubDetectionRuntime(), new StubInputController());

        await controller.StartAsync(CancellationToken.None);
        await controller.StopAsync(CancellationToken.None);

        Assert.Equal(InferenceBackend.Unavailable, controller.Snapshot.Execution.Backend);
        Assert.Equal(InferenceBackend.Cpu, controller.Snapshot.LastSuccessfulExecution?.Backend);
        Assert.Equal("CPU", controller.Snapshot.LastSuccessfulExecution?.DeviceName);
    }

    [Fact]
    public void Execution_history_requires_a_new_run_after_the_requested_device_changes()
    {
        var previous = new ExecutionRuntimeInfo(
            ExecutionDevice.Cpu,
            InferenceBackend.Cpu,
            "CPU",
            false,
            null);

        Assert.Equal(
            ExecutionHistoryState.Confirmed,
            ExecutionRuntimeInfo.GetHistoryState(previous, ExecutionDevice.Cpu));
        Assert.Equal(
            ExecutionHistoryState.AwaitingConfirmation,
            ExecutionRuntimeInfo.GetHistoryState(previous, ExecutionDevice.Gpu));
        Assert.Equal(
            ExecutionHistoryState.NoRun,
            ExecutionRuntimeInfo.GetHistoryState(null, ExecutionDevice.Auto));
    }

    [Fact]
    public async Task Stop_during_start_cancels_the_transaction_and_is_idempotent()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detection = new StubDetectionRuntime
        {
            PrepareHandler = async cancellationToken =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var input = new StubInputController();
        var controller = Create(new StubModelCatalog(), detection, input);

        var start = controller.StartAsync(CancellationToken.None);
        await entered.Task;
        var stop = controller.StopAsync(CancellationToken.None);
        await Task.WhenAll(start, stop);
        await controller.StopAsync(CancellationToken.None);

        Assert.Equal(RuntimeLifecycle.Stopped, controller.Snapshot.Lifecycle);
        Assert.Equal(0, detection.ActivateCount);
        Assert.True(detection.StopCount >= 2);
        Assert.True(input.ReleaseCount >= 2);
    }

    [Fact]
    public void Model_readiness_change_clears_and_restores_the_model_warning()
    {
        var models = new StubModelCatalog { Ready = false };
        var controller = Create(models, new StubDetectionRuntime(), new StubInputController());

        Assert.False(controller.Snapshot.ModelsReady);
        Assert.Equal(RuntimeMessageCode.ModelsUnavailable, controller.Snapshot.Status.Code);

        models.SetReady(true);

        Assert.True(controller.Snapshot.ModelsReady);
        Assert.Equal(RuntimeMessageCode.Stopped, controller.Snapshot.Status.Code);

        models.SetReady(false);

        Assert.False(controller.Snapshot.ModelsReady);
        Assert.Equal(RuntimeMessageCode.ModelsUnavailable, controller.Snapshot.Status.Code);
    }

    private static RuntimeController Create(
        StubModelCatalog models,
        StubDetectionRuntime detection,
        StubInputController input) => new(
            models,
            detection,
            input,
            NullLogger<RuntimeController>.Instance);

    private sealed class StubInputController : IInputController
    {
        public bool IsTargetForeground { get; set; } = true;
        public int ReleaseCount { get; private set; }
        public InputExecutionResult Click() => InputExecutionResult.NoChange;
        public InputExecutionResult PressLeft() => InputExecutionResult.NoChange;
        public InputExecutionResult ReleaseLeft() => InputExecutionResult.NoChange;
        public InputExecutionResult ReleaseAll()
        {
            ReleaseCount++;
            return InputExecutionResult.NoChange;
        }
    }

    private sealed class StubDetectionRuntime : IDetectionRuntime
    {
        public ExecutionRuntimeInfo Execution { get; private set; } =
            new(ExecutionDevice.Cpu, InferenceBackend.Cpu, "CPU", false, null);
        public bool IsReady => true;
        public int PrepareCount { get; private set; }
        public int ActivateCount { get; private set; }
        public int StopCount { get; private set; }
        public Func<CancellationToken, Task>? PrepareHandler { get; init; }
        public event EventHandler<DetectionRuntimeMetrics>? MetricsChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<DetectionVisualizationFrame>? VisualizationChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<FishingOperationTrace>? FishingOperationSubmitted
        {
            add { }
            remove { }
        }

        public async Task PrepareAsync(CancellationToken cancellationToken)
        {
            PrepareCount++;
            if (PrepareHandler is not null) await PrepareHandler(cancellationToken);
        }

        public void Activate() => ActivateCount++;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubModelCatalog : IModelCatalog
    {
        public event EventHandler? StatusChanged;
        public bool Ready { get; set; } = true;
        public bool IsReady => Ready;
        public bool AutomaticAllowed => Ready;
        public long InstalledSize => 0;
        public string Repository => "test/repository";
        public string? InstalledVersion => "test";
        public string? LatestVersion => "test";
        public bool UpdateAvailable => false;
        public bool UpdateCheckSucceeded => true;
        public Uri SourceUri => new("https://example.invalid/");
        public int RefreshCount { get; private set; }

        public void SetReady(bool ready)
        {
            Ready = ready;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public IReadOnlyList<ModelStatus> GetStatus() => [];

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            RefreshCount++;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task CheckForUpdatesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ModelManifest> DownloadLatestAsync(
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteModelsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
