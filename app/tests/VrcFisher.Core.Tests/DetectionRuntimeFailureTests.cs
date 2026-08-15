using Microsoft.Extensions.Logging.Abstractions;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Infrastructure.Capture;
using VrcFisher.Infrastructure.Runtime;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class DetectionRuntimeFailureTests
{
    [Fact]
    public async Task Capture_failure_subscriber_cannot_escape_the_capture_callback_boundary()
    {
        await using var source = new WindowsGraphicsCaptureSource();
        source.Configure("VRChat");
        await source.StartAsync(CancellationToken.None);
        source.CaptureFailed += (_, _) => throw new InvalidOperationException("subscriber failed");

        var callbackError = Record.Exception(
            () => source.PublishCaptureFailure(new InvalidOperationException("capture failed")));

        Assert.Null(callbackError);
    }

    [Fact]
    public async Task Capture_failure_before_first_frame_faults_preparation_and_releases_input()
    {
        var capture = new StubFrameSource();
        capture.StartHandler = _ =>
        {
            capture.Fail(new InvalidCastException("buffer interface unavailable"));
            return Task.CompletedTask;
        };
        var input = new StubInputController();
        await using var fixture = CreateRuntime(capture, input);
        var runtime = fixture.Runtime;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.PrepareAsync(CancellationToken.None));

        Assert.Contains("buffer interface unavailable", error.ToString());
        Assert.True(input.ReleaseCount >= 1);
        Assert.True(capture.StopCount >= 1);
    }

    [Fact]
    public async Task Capture_failure_while_running_rolls_back_without_escaping_the_callback()
    {
        var capture = new StubFrameSource();
        capture.StartHandler = _ =>
        {
            capture.PublishFrame();
            return Task.CompletedTask;
        };
        var input = new StubInputController();
        await using var fixture = CreateRuntime(capture, input);
        var runtime = fixture.Runtime;
        var controller = new RuntimeController(
            new StubModelCatalog(),
            runtime,
            input,
            NullLogger<RuntimeController>.Instance);
        await controller.StartAsync(CancellationToken.None);

        var callbackError = Record.Exception(
            () => capture.Fail(new InvalidOperationException("frame conversion failed")));

        Assert.Null(callbackError);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (controller.Snapshot.Lifecycle != RuntimeLifecycle.Stopped
               || controller.Snapshot.Status.Code != RuntimeMessageCode.DetectionStopped)
        {
            await Task.Delay(20, timeout.Token);
        }

        Assert.Contains("frame conversion failed", controller.Snapshot.Status.Detail);
        Assert.True(input.ReleaseCount >= 1);
        Assert.True(capture.StopCount >= 1);
    }

    private static TestRuntime CreateRuntime(StubFrameSource capture, StubInputController input)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
        var layout = new DirectoryLayout(root);
        layout.Ensure();
        var runtime = new DetectionRuntime(
            layout,
            () => AppOptions.Default with { Device = ExecutionDevice.Cpu },
            capture,
            new StubModelCatalog(),
            input,
            NullLogger<DetectionRuntime>.Instance,
            _ => new StubDetector());
        return new TestRuntime(runtime, root);
    }

    private sealed class TestRuntime(DetectionRuntime runtime, string root) : IAsyncDisposable
    {
        public DetectionRuntime Runtime { get; } = runtime;

        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubFrameSource : IFrameSource
    {
        private long _sequence;
        public Func<CancellationToken, Task>? StartHandler { get; set; }
        public int StopCount { get; private set; }
        public event EventHandler<CapturedFrameEventArgs>? FrameArrived;
        public event EventHandler<FrameSourceFailedEventArgs>? CaptureFailed;

        public Task StartAsync(CancellationToken cancellationToken) =>
            StartHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void PublishFrame() => FrameArrived?.Invoke(
            this,
            new CapturedFrameEventArgs(
                Interlocked.Increment(ref _sequence),
                DateTimeOffset.UtcNow,
                new byte[4],
                1,
                1));

        public void Fail(Exception error) =>
            CaptureFailed?.Invoke(this, new FrameSourceFailedEventArgs(error));
    }

    private sealed class StubDetector : IDetector
    {
        public ExecutionRuntimeInfo Execution { get; } =
            new(ExecutionDevice.Cpu, InferenceBackend.Cpu, "CPU", false, null);
        public bool IsReady => true;
        public bool CanProduceDecisions => true;
        public bool HasCachedPanel => false;

        public DetectionResult Detect(
            CapturedFrameEventArgs frame,
            FishingPhase phase,
            TimeSpan minigamePanelRecheckInterval,
            bool includeVisualization = false) => new(
                new DetectionObservation(frame.FrameNumber, frame.CapturedAt),
                InferenceWorkload.Locator,
                null);

        public void Dispose() { }
    }

    private sealed class StubInputController : IInputController
    {
        public bool IsTargetForeground => true;
        public int ReleaseCount { get; private set; }
        public void Click() { }
        public void PressLeft() { }
        public void ReleaseLeft() { }
        public void ReleaseAll() => ReleaseCount++;
    }

    private sealed class StubModelCatalog : IModelCatalog
    {
        public event EventHandler? StatusChanged;
        public bool IsReady => true;
        public bool AutomaticAllowed => true;
        public long InstalledSize => 0;
        public string Repository => "test/repository";
        public string? InstalledVersion => "test";
        public string? LatestVersion => "test";
        public bool UpdateAvailable => false;
        public bool UpdateCheckSucceeded => true;
        public Uri SourceUri => new("https://example.invalid/");
        public IReadOnlyList<ModelStatus> GetStatus() => [];
        public Task RefreshAsync(CancellationToken cancellationToken)
        {
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
