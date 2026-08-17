using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Infrastructure.Capture;
using VrcFisher.Infrastructure.Logging;
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

    [Fact]
    public async Task Demand_capture_requests_the_first_frame_immediately()
    {
        var capture = new StubFrameSource();
        capture.StartHandler = _ =>
        {
            capture.PublishFrame();
            return Task.CompletedTask;
        };
        await using var fixture = CreateRuntime(capture, new StubInputController());

        await fixture.Runtime.PrepareAsync(CancellationToken.None);

        Assert.Equal([TimeSpan.Zero], capture.Requests);
    }

    [Fact]
    public async Task Demand_capture_requests_the_next_frame_after_processing()
    {
        var capture = new StubFrameSource();
        capture.StartHandler = _ =>
        {
            capture.PublishFrame();
            return Task.CompletedTask;
        };
        await using var fixture = CreateRuntime(capture, new StubInputController());
        await fixture.Runtime.PrepareAsync(CancellationToken.None);

        fixture.Runtime.Activate();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (capture.Requests.Count < 2)
            await Task.Delay(10, timeout.Token);
        Assert.Equal(TimeSpan.Zero, capture.Requests[0]);
        Assert.True(capture.Requests[1] >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Demand_capture_does_not_request_frames_after_stop()
    {
        var capture = new StubFrameSource();
        capture.StartHandler = _ =>
        {
            capture.PublishFrame();
            return Task.CompletedTask;
        };
        await using var fixture = CreateRuntime(capture, new StubInputController());
        await fixture.Runtime.PrepareAsync(CancellationToken.None);
        fixture.Runtime.Activate();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (capture.Requests.Count < 2)
            await Task.Delay(10, timeout.Token);

        await fixture.Runtime.StopAsync(CancellationToken.None);
        var requestCount = capture.Requests.Count;
        capture.PublishFrame();
        await Task.Delay(50);

        Assert.Equal(requestCount, capture.Requests.Count);
        Assert.Equal(0, capture.RequestsAfterStop);
    }

    [Fact]
    public async Task Failed_initial_cast_stops_the_runtime_without_continuing_the_cycle()
    {
        var capture = new StubFrameSource();
        capture.StartHandler = _ =>
        {
            capture.PublishFrame();
            return Task.CompletedTask;
        };
        var input = new StubInputController
        {
            ClickResult = InputExecutionResult.Failure(0, 2, "input rejected")
        };
        await using var fixture = CreateRuntime(capture, input);
        var controller = new RuntimeController(
            new StubModelCatalog(),
            fixture.Runtime,
            input,
            NullLogger<RuntimeController>.Instance);

        await controller.StartAsync(CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (controller.Snapshot.Lifecycle != RuntimeLifecycle.Stopped
               || controller.Snapshot.Status.Code != RuntimeMessageCode.InputFailed)
        {
            await Task.Delay(20, timeout.Token);
        }
        var clickCount = input.ClickCount;
        await Task.Delay(100, timeout.Token);

        Assert.Equal(1, clickCount);
        Assert.Equal(clickCount, input.ClickCount);
        Assert.True(input.ReleaseCount >= 1);
        Assert.Contains("input rejected", controller.Snapshot.Status.Detail);
    }

    [Fact]
    public async Task First_processed_frame_submits_exactly_one_initial_cast()
    {
        var capture = new StubFrameSource();
        capture.StartHandler = _ =>
        {
            capture.PublishFrame();
            return Task.CompletedTask;
        };
        var input = new StubInputController();
        await using var fixture = CreateRuntime(capture, input);
        var controller = new RuntimeController(
            new StubModelCatalog(),
            fixture.Runtime,
            input,
            NullLogger<RuntimeController>.Instance);

        await controller.StartAsync(CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (input.ClickCount == 0)
            await Task.Delay(20, timeout.Token);

        Assert.Equal(1, input.ClickCount);
        Assert.Equal(FishingPhase.Casting, controller.Snapshot.Phase);
        await controller.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Debug_log_correlates_inference_decision_and_input()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
        var layout = new DirectoryLayout(root);
        layout.Ensure();
        var capture = new StubFrameSource();
        capture.StartHandler = _ =>
        {
            capture.PublishFrame();
            return Task.CompletedTask;
        };
        var input = new StubInputController();
        using var logs = new FileLoggerProvider(layout.Logs, ApplicationMode.Debug);
        await using var runtime = new DetectionRuntime(
            layout,
            () => AppOptions.Default with
            {
                Device = ExecutionDevice.Cpu,
                WorkMode = ApplicationMode.Debug
            },
            capture,
            new StubModelCatalog(),
            input,
            new ForwardingLogger<DetectionRuntime>(logs.CreateLogger(typeof(DetectionRuntime).FullName!)),
            _ => new StubDetector());
        var controller = new RuntimeController(
            new StubModelCatalog(),
            runtime,
            input,
            NullLogger<RuntimeController>.Instance);
        FishingOperationTrace? submittedOperation = null;
        runtime.FishingOperationSubmitted += (_, operation) => submittedOperation = operation;

        try
        {
            await controller.StartAsync(CancellationToken.None);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (input.ClickCount == 0)
                await Task.Delay(20, timeout.Token);
            await controller.StopAsync(CancellationToken.None);
            await runtime.DisposeAsync();
            logs.Dispose();

            var text = File.ReadAllText(Path.Combine(layout.Logs, "debug", "current.log"));
            Assert.Contains("inference session=", text);
            Assert.Contains("cycle=1 frame=1", text);
            Assert.Contains("decision_id=1 source_frame=1", text);
            Assert.Contains("input session=", text);
            Assert.Contains("decision_id=1 action=Click", text);
            Assert.Contains("fishing_operation_requested session=", text);
            Assert.Contains("operation_id=1 cycle=1 operation=cast decision_id=1", text);
            Assert.Contains("fishing_operation_input session=", text);
            Assert.NotNull(submittedOperation);
            Assert.Equal(1, submittedOperation.OperationId);
            Assert.Equal(1, submittedOperation.Cycle);
            Assert.Equal(FishingOperationKind.Cast, submittedOperation.Operation);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(ApplicationMode.Run, false)]
    [InlineData(ApplicationMode.Debug, true)]
    public async Task Runtime_infers_during_pulse_and_immediately_reacts_to_post_release_frames(
        ApplicationMode mode,
        bool expectsVisualization)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
        var layout = new DirectoryLayout(root);
        layout.Ensure();
        var capture = new StubFrameSource();
        capture.RequestHandler = delay => _ = Task.Run(async () =>
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
            capture.PublishFrame();
        });
        var pulseState = new PulseTestState();
        var input = new StubInputController { PulseState = pulseState };
        var detector = new PulseDetector(pulseState);
        var firstPulseRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pulseCount = 0;
        async Task PulseDelay(TimeSpan duration, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref pulseCount) == 1)
                await firstPulseRelease.Task.WaitAsync(cancellationToken);
            else
                await Task.Delay(duration, cancellationToken);
        }

        await using var runtime = new DetectionRuntime(
            layout,
            () => AppOptions.Default with
            {
                Device = ExecutionDevice.Cpu,
                WorkMode = mode
            },
            capture,
            new StubModelCatalog(),
            input,
            NullLogger<DetectionRuntime>.Instance,
            _ => detector,
            PulseDelay);
        var controller = new RuntimeController(
            new StubModelCatalog(),
            runtime,
            input,
            NullLogger<RuntimeController>.Instance);
        var visualizationCount = 0;
        runtime.VisualizationChanged += (_, _) => Interlocked.Increment(ref visualizationCount);

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await controller.StartAsync(timeout.Token);
            await pulseState.FirstPress.Task.WaitAsync(timeout.Token);
            var countAtPress = Volatile.Read(ref visualizationCount);

            await pulseState.DetectedWhileHeld.Task.WaitAsync(timeout.Token);
            if (expectsVisualization)
            {
                while (Volatile.Read(ref visualizationCount) <= countAtPress)
                    await Task.Delay(5, timeout.Token);
            }

            firstPulseRelease.SetResult();
            await pulseState.FirstRelease.Task.WaitAsync(timeout.Token);
            while (pulseState.PressCount < 2)
                await Task.Delay(5, timeout.Token);

            Assert.True(pulseState.DetectionsWhileHeld > 0);
            Assert.True(pulseState.SecondPressFrame > pulseState.FirstReleaseAt);
            Assert.Equal(expectsVisualization, Volatile.Read(ref visualizationCount) > 0);
        }
        finally
        {
            firstPulseRelease.TrySetResult();
            await controller.StopAsync(CancellationToken.None);
            Assert.Equal(0, capture.RequestsAfterStop);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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

    private sealed class StubFrameSource : IDemandDrivenFrameSource
    {
        private readonly object _sync = new();
        private long _sequence;
        private bool _stopped;
        public Func<CancellationToken, Task>? StartHandler { get; set; }
        public Action<TimeSpan>? RequestHandler { get; set; }
        public int StopCount { get; private set; }
        public int RequestsAfterStop { get; private set; }
        public IReadOnlyList<TimeSpan> Requests
        {
            get { lock (_sync) return _requests.ToArray(); }
        }
        private readonly List<TimeSpan> _requests = [];
        public event EventHandler<CapturedFrameEventArgs>? FrameArrived;
        public event EventHandler<FrameSourceFailedEventArgs>? CaptureFailed;

        public Task StartAsync(CancellationToken cancellationToken) =>
            StartHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                StopCount++;
                _stopped = true;
            }
            return Task.CompletedTask;
        }

        public void RequestNextFrame(TimeSpan delay)
        {
            Action<TimeSpan>? handler;
            lock (_sync)
            {
                if (_stopped) RequestsAfterStop++;
                _requests.Add(delay);
                handler = _stopped ? null : RequestHandler;
            }
            handler?.Invoke(delay);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void PublishFrame() => FrameArrived?.Invoke(
            this,
            new CapturedFrameEventArgs(
                Interlocked.Increment(ref _sequence),
                DateTimeOffset.UtcNow,
                new byte[4],
                1,
                1,
                Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp())));

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
                new DetectionObservation(
                    frame.FrameNumber,
                    frame.CapturedAt,
                    CapturedTimestamp: frame.CapturedTimestamp),
                InferenceWorkload.Locator,
                null);

        public void Dispose() { }
    }

    private sealed class StubInputController : IInputController
    {
        private readonly object _sync = new();
        private bool _held;
        public bool IsTargetForeground => true;
        public int ReleaseCount { get; private set; }
        public int ClickCount { get; private set; }
        public PulseTestState? PulseState { get; init; }
        public InputExecutionResult ClickResult { get; init; } = InputExecutionResult.NoChange;
        public InputExecutionResult Click()
        {
            ClickCount++;
            return ClickResult;
        }
        public InputExecutionResult PressLeft()
        {
            lock (_sync)
            {
                if (PulseState is null || _held) return InputExecutionResult.NoChange;
                _held = true;
                PulseState.RecordPress();
                return InputExecutionResult.Success(1, 1);
            }
        }
        public InputExecutionResult ReleaseLeft()
        {
            lock (_sync)
            {
                if (!_held) return InputExecutionResult.NoChange;
                _held = false;
                PulseState?.RecordRelease();
                return InputExecutionResult.Success(1, 1);
            }
        }
        public InputExecutionResult ReleaseAll()
        {
            ReleaseCount++;
            return ReleaseLeft();
        }
    }

    private sealed class PulseTestState
    {
        private readonly object _sync = new();
        private DateTimeOffset _latestDetectionAt;
        private int _detectionsWhileHeld;
        private bool _held;
        public TaskCompletionSource FirstPress { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DetectedWhileHeld { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<DateTimeOffset> _pressFrames = [];
        private readonly List<DateTimeOffset> _releaseTimes = [];
        public int DetectionsWhileHeld => Volatile.Read(ref _detectionsWhileHeld);
        public int PressCount
        {
            get { lock (_sync) return _pressFrames.Count; }
        }
        public DateTimeOffset SecondPressFrame
        {
            get { lock (_sync) return _pressFrames[1]; }
        }
        public DateTimeOffset FirstReleaseAt
        {
            get { lock (_sync) return _releaseTimes[0]; }
        }

        public void RecordDetection(DateTimeOffset capturedAt)
        {
            lock (_sync)
            {
                _latestDetectionAt = capturedAt;
                if (!_held) return;
                Interlocked.Increment(ref _detectionsWhileHeld);
                DetectedWhileHeld.TrySetResult();
            }
        }

        public void RecordPress()
        {
            lock (_sync)
            {
                _held = true;
                _pressFrames.Add(_latestDetectionAt);
                FirstPress.TrySetResult();
            }
        }

        public void RecordRelease()
        {
            lock (_sync)
            {
                _held = false;
                _releaseTimes.Add(DateTimeOffset.UtcNow);
                FirstRelease.TrySetResult();
            }
        }
    }

    private sealed class PulseDetector(PulseTestState state) : IDetector
    {
        private static readonly BoundingBox Panel = new(0, 0, 100, 100);
        private static readonly BoundingBox Zone = new(10, 40, 30, 60);
        private static readonly BoundingBox Target = new(10, 35, 30, 39);

        public ExecutionRuntimeInfo Execution { get; } =
            new(ExecutionDevice.Cpu, InferenceBackend.Cpu, "CPU", false, null);
        public bool IsReady => true;
        public bool CanProduceDecisions => true;
        public bool HasCachedPanel => true;

        public DetectionResult Detect(
            CapturedFrameEventArgs frame,
            FishingPhase phase,
            TimeSpan minigamePanelRecheckInterval,
            bool includeVisualization = false)
        {
            state.RecordDetection(frame.CapturedAt);
            var observation = phase switch
            {
                FishingPhase.WaitingForBite => new DetectionObservation(
                    frame.FrameNumber,
                    frame.CapturedAt,
                    BiteIndicator: new BoundingBox(1, 1, 5, 5),
                    CapturedTimestamp: frame.CapturedTimestamp),
                FishingPhase.Hooking => new DetectionObservation(
                    frame.FrameNumber,
                    frame.CapturedAt,
                    MinigamePanel: Panel,
                    CapturedTimestamp: frame.CapturedTimestamp),
                FishingPhase.Minigame => new DetectionObservation(
                    frame.FrameNumber,
                    frame.CapturedAt,
                    MinigamePanel: Panel,
                    CatchZone: Zone,
                    MovingTarget: Target,
                    PanelGeneration: 1,
                    CapturedTimestamp: frame.CapturedTimestamp),
                _ => new DetectionObservation(
                    frame.FrameNumber,
                    frame.CapturedAt,
                    CapturedTimestamp: frame.CapturedTimestamp)
            };
            var visualization = includeVisualization
                ? new DetectionVisualizationFrame(
                    frame.FrameNumber,
                    frame.CapturedAt,
                    frame.Width,
                    frame.Height,
                    [new DetectionVisual("moving_target", 0.9f, Target)])
                : null;
            return new DetectionResult(observation, InferenceWorkload.CachedMinigame, visualization);
        }

        public void Dispose() { }
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

    private sealed class ForwardingLogger<T>(Microsoft.Extensions.Logging.ILogger inner)
        : Microsoft.Extensions.Logging.ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            inner.BeginScope(state);

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) =>
            inner.IsEnabled(logLevel);

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
