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
        var controller = new RuntimeController(
            models,
            detection,
            input,
            NullLogger<RuntimeController>.Instance);

        await controller.StartAsync(CancellationToken.None);

        Assert.Equal(RuntimeMessageCode.StartTargetNotForeground, controller.Snapshot.Status.Code);
        Assert.False(controller.Snapshot.IsObserving);
        Assert.Equal(0, models.RefreshCount);
        Assert.Equal(0, detection.StartCount);
    }

    private sealed class StubInputController : IInputController
    {
        public bool IsTargetForeground { get; set; }
        public void Click() { }
        public void PressLeft() { }
        public void ReleaseLeft() { }
        public void ReleaseAll() { }
    }

    private sealed class StubDetectionRuntime : IDetectionRuntime
    {
        public string Provider => "CPUExecutionProvider";
        public bool IsReady => true;
        public int StartCount { get; private set; }
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

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubModelCatalog : IModelCatalog
    {
        public bool IsReady => true;
        public bool AutomaticAllowed => true;
        public long InstalledSize => 0;
        public string Repository => "test/repository";
        public string? InstalledVersion => "test";
        public string? LatestVersion => "test";
        public bool UpdateAvailable => false;
        public bool UpdateCheckSucceeded => true;
        public Uri SourceUri => new("https://example.invalid/");
        public int RefreshCount { get; private set; }

        public IReadOnlyList<ModelStatus> GetStatus() => [];

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }

        public Task CheckForUpdatesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ModelManifest> DownloadLatestAsync(
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteModelsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
