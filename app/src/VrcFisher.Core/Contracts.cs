using System.Text.Json.Serialization;

namespace VrcFisher.Core;

public enum FishingPhase
{
    Idle,
    Casting,
    WaitingForBite,
    Hooking,
    Minigame,
    Reeling,
    Loot,
    Recovery,
    Stopped
}

public enum InputAction
{
    None,
    Click,
    Press,
    Release,
    Pulse
}

public enum MinigameInputState
{
    Released,
    Pressed,
    Cooldown
}

public sealed record MinigameDynamicsParameters(
    double? ReleaseAcceleration = null,
    double? PressAcceleration = null)
{
    public static MinigameDynamicsParameters Empty => new();

    public MinigameDynamicsParameters Normalize() => new(
        NormalizeRelease(ReleaseAcceleration),
        NormalizePress(PressAcceleration));

    private static double? NormalizeRelease(double? value) =>
        value is < 0 and >= -40 && double.IsFinite(value.Value) ? value : null;

    private static double? NormalizePress(double? value) =>
        value is > 0 and <= 40 && double.IsFinite(value.Value) ? value : null;
}

public enum ExecutionDevice
{
    Auto,
    Cpu,
    Gpu
}

public enum InferenceBackend
{
    Unavailable,
    Cpu,
    DirectML
}

public enum ExecutionHistoryState
{
    NoRun,
    AwaitingConfirmation,
    Confirmed
}

public enum RuntimeLifecycle
{
    Stopped,
    Starting,
    Running,
    Stopping
}

public enum ApplicationMode
{
    Run,
    Debug
}

public enum FishingOperationKind
{
    Cast,
    Reel
}

public enum InferenceWorkload
{
    Locator,
    LocatorAndMinigame,
    CachedMinigame
}

public enum RuntimeMessageCode
{
    VrChatNotRunning,
    StartTargetNotForeground,
    HotkeyRegistrationFailed,
    OverlayUnavailable,
    UnexpectedFailure,
    ModelsUnavailable,
    ModelsRequired,
    AutomaticNotAllowed,
    Starting,
    AutomaticStarted,
    Stopping,
    DetectionStopped,
    Stopped,
    CaptureStopped,
    FrameStale,
    TargetNotForeground,
    OutputContractUnverified,
    InferenceFailed,
    InputFailed,
    StateMachineDecision
}

public readonly record struct BoundingBox(float Left, float Top, float Right, float Bottom)
{
    public float CenterX => (Left + Right) / 2f;
    public float CenterY => (Top + Bottom) / 2f;
    public float Width => MathF.Max(0, Right - Left);
    public float Height => MathF.Max(0, Bottom - Top);
}

public sealed record DetectionObservation(
    long FrameNumber,
    DateTimeOffset CapturedAt,
    BoundingBox? BiteIndicator = null,
    BoundingBox? MinigamePanel = null,
    BoundingBox? CatchZone = null,
    BoundingBox? MovingTarget = null,
    float? MovingTargetYNorm = null,
    float? CatchZoneTopNorm = null,
    float? CatchZoneBottomNorm = null,
    float? BiteIndicatorConfidence = null,
    float? MinigamePanelConfidence = null,
    float? CatchZoneConfidence = null,
    float? MovingTargetConfidence = null,
    long PanelGeneration = 0,
    TimeSpan CapturedTimestamp = default)
{
    public bool HasBiteIndicator => BiteIndicator is not null;
    public bool HasMinigamePanel => MinigamePanel is not null;
}

public readonly record struct StateDecision(
    FishingPhase Phase,
    InputAction Action,
    string Reason,
    int Cycle,
    TimeSpan? PredictedReleaseDelay = null,
    string? Diagnostic = null,
    TimeSpan MinimumPulseDuration = default,
    TimeSpan? PredictedRepressDelay = null,
    TimeSpan ControlPlanHorizon = default,
    TimeSpan FeedbackTimeout = default,
    bool HasFreshControlFeedback = false);

public sealed record StateMachineOptions(
    int BiteIndicatorConfirmFrames = 3,
    int BiteIndicatorEvidenceWindow = 5,
    int UiConfirmFrames = 3,
    int UiLostFrames = 5,
    TimeSpan CastSettle = default,
    TimeSpan BiteFallback = default,
    TimeSpan BiteTimeout = default,
    TimeSpan HookToUiMinimum = default,
    TimeSpan BiteToMinigameTimeout = default,
    TimeSpan MinigameTimeout = default,
    TimeSpan LootTimeout = default,
    TimeSpan RecoveryDelay = default,
    TimeSpan ReelReadyDelay = default,
    TimeSpan PostReelDelay = default)
{
    public static StateMachineOptions Default => new(
        BiteIndicatorEvidenceWindow: 5,
        CastSettle: TimeSpan.FromMilliseconds(450),
        BiteFallback: TimeSpan.Zero,
        BiteTimeout: TimeSpan.FromSeconds(20),
        HookToUiMinimum: TimeSpan.FromMilliseconds(120),
        BiteToMinigameTimeout: TimeSpan.FromSeconds(3),
        MinigameTimeout: TimeSpan.FromSeconds(30),
        LootTimeout: TimeSpan.FromSeconds(5),
        RecoveryDelay: TimeSpan.FromSeconds(1),
        ReelReadyDelay: TimeSpan.FromSeconds(1),
        PostReelDelay: TimeSpan.FromSeconds(2));
}

public interface IStateMachine
{
    FishingPhase Phase { get; }
    StateDecision Step(DetectionObservation observation, DateTimeOffset now);
    StateDecision Stop(DateTimeOffset now);
}

public interface IInputController
{
    bool IsTargetForeground { get; }
    InputExecutionResult Click();
    InputExecutionResult PressLeft();
    InputExecutionResult ReleaseLeft();
    InputExecutionResult ReleaseAll();
}

public readonly record struct InputExecutionResult(
    bool Succeeded,
    int SubmittedEvents,
    int ExpectedEvents,
    string? Error = null)
{
    public DateTimeOffset? PressedAt { get; init; }
    public DateTimeOffset? ReleasedAt { get; init; }

    public static InputExecutionResult Success(int submittedEvents, int expectedEvents) =>
        new(true, submittedEvents, expectedEvents);

    public static InputExecutionResult NoChange => new(true, 0, 0);

    public static InputExecutionResult Failure(
        int submittedEvents,
        int expectedEvents,
        string error) => new(false, submittedEvents, expectedEvents, error);
}

public interface IFrameSource : IAsyncDisposable
{
    event EventHandler<CapturedFrameEventArgs>? FrameArrived;
    event EventHandler<FrameSourceFailedEventArgs>? CaptureFailed;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IDemandDrivenFrameSource : IFrameSource
{
    void RequestNextFrame(TimeSpan delay);
}

public sealed class FrameSourceFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception ?? throw new ArgumentNullException(nameof(exception));
}

public sealed class CapturedFrameEventArgs(
    long frameNumber,
    DateTimeOffset capturedAt,
    ReadOnlyMemory<byte> bgraPixels,
    int width,
    int height,
    TimeSpan capturedTimestamp = default) : EventArgs
{
    public long FrameNumber { get; } = frameNumber;
    public DateTimeOffset CapturedAt { get; } = capturedAt;
    public TimeSpan CapturedTimestamp { get; } = capturedTimestamp;
    public ReadOnlyMemory<byte> BgraPixels { get; } = bgraPixels;
    public int Width { get; } = width;
    public int Height { get; } = height;
}

public interface IDetector : IDisposable
{
    ExecutionRuntimeInfo Execution { get; }
    bool IsReady { get; }
    bool CanProduceDecisions { get; }
    bool HasCachedPanel { get; }
    DetectionResult Detect(
        CapturedFrameEventArgs frame,
        FishingPhase phase,
        TimeSpan minigamePanelRecheckInterval,
        bool includeVisualization = false);
}

public sealed record DetectionVisual(
    string ClassName,
    float Confidence,
    BoundingBox Box);

public sealed record DetectionVisualizationFrame(
    long FrameNumber,
    DateTimeOffset CapturedAt,
    int Width,
    int Height,
    IReadOnlyList<DetectionVisual> Detections);

public sealed record FishingOperationTrace(
    long OperationId,
    int Cycle,
    FishingOperationKind Operation,
    DateTimeOffset SubmittedAt);

public readonly record struct DetectionResult(
    DetectionObservation Observation,
    InferenceWorkload Workload,
    DetectionVisualizationFrame? Visualization);

public sealed record ModelFileInfo(
    [property: JsonPropertyName("filename")] string FileName,
    long Size,
    string Sha256);

public sealed record ModelManifest(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("runtime_api")] int RuntimeApi,
    string Version,
    IReadOnlyList<ModelFileInfo> Models,
    IReadOnlyList<ModelFileInfo> Documentation,
    [property: JsonPropertyName("automatic_allowed")] bool AutomaticAllowed = false);

public sealed record ModelStatus(
    string Name,
    bool Installed,
    bool Valid,
    long Size,
    string? Version,
    string Message);

public sealed record ModelDownloadProgress(
    string CurrentFile,
    long BytesDownloaded,
    long BytesTotal,
    int CompletedFiles,
    int TotalFiles);

public sealed record RuntimeSnapshot(
    RuntimeLifecycle Lifecycle,
    FishingPhase Phase,
    bool IsObserving,
    bool IsAutomatic,
    bool ModelsReady,
    ExecutionRuntimeInfo Execution,
    ExecutionRuntimeInfo? LastSuccessfulExecution,
    long FramesCaptured,
    long FramesDropped,
    InferencePerformanceSnapshot Performance,
    RuntimeStatus Status,
    DateTimeOffset UpdatedAt);

public sealed record ExecutionRuntimeInfo(
    ExecutionDevice Requested,
    InferenceBackend Backend,
    string? DeviceName,
    bool FellBack,
    string? FallbackReason)
{
    public static ExecutionRuntimeInfo Unavailable(ExecutionDevice requested = ExecutionDevice.Auto) =>
        new(requested, InferenceBackend.Unavailable, null, false, null);

    public string ProfileKey => Backend switch
    {
        InferenceBackend.DirectML => "directml",
        InferenceBackend.Cpu => "cpu",
        _ => "unavailable"
    };

    public static ExecutionHistoryState GetHistoryState(
        ExecutionRuntimeInfo? lastSuccessfulExecution,
        ExecutionDevice requested) => lastSuccessfulExecution switch
        {
            null => ExecutionHistoryState.NoRun,
            { Requested: var previous } when previous != requested => ExecutionHistoryState.AwaitingConfirmation,
            _ => ExecutionHistoryState.Confirmed
        };
}

public sealed record GraphicsAdapterInfo(
    int Index,
    string Name,
    long DedicatedMemoryBytes,
    string? DriverVersion);

public sealed record HardwareSnapshot(
    string CpuName,
    int PhysicalCores,
    int LogicalProcessors,
    IReadOnlyList<GraphicsAdapterInfo> GraphicsAdapters,
    long TotalMemoryBytes,
    string WindowsVersion,
    bool IsX64,
    string? Error = null)
{
    public static HardwareSnapshot Unavailable(string? error = null) => new(
        "Unavailable",
        0,
        Environment.ProcessorCount,
        [],
        0,
        "Unavailable",
        Environment.Is64BitOperatingSystem,
        error);
}

public interface IHardwareInfoProvider
{
    Task<HardwareSnapshot> ReadAsync(CancellationToken cancellationToken);
}

public sealed record RuntimeStatus(RuntimeMessageCode Code, string? Detail = null);

public sealed record DetectionRuntimeMetrics(
    long FramesCaptured,
    long FramesDropped,
    FishingPhase Phase,
    InferencePerformanceSnapshot Performance,
    RuntimeStatus Status,
    DateTimeOffset UpdatedAt);

public readonly record struct InferencePerformanceSnapshot(
    bool Adaptive,
    bool ProfileLoaded,
    bool IsCalibrating,
    bool PerformanceInsufficient,
    int LocatorIntervalMs,
    int HookingIntervalMs,
    int MinigameIntervalMs,
    int PanelRecheckIntervalMs,
    double? LocatorP95Ms = null,
    double? LocatorAndMinigameP95Ms = null,
    double? CachedMinigameP95Ms = null,
    double LastFrameAgeMs = 0,
    long InferenceOverruns = 0,
    long RecentFramesDropped = 0)
{
    public static InferencePerformanceSnapshot Default => new(
        Adaptive: true,
        ProfileLoaded: false,
        IsCalibrating: true,
        PerformanceInsufficient: false,
        LocatorIntervalMs: 80,
        HookingIntervalMs: 80,
        MinigameIntervalMs: 40,
        PanelRecheckIntervalMs: 250);
}

public interface IRuntimeController
{
    RuntimeSnapshot Snapshot { get; }
    event EventHandler<RuntimeSnapshot>? SnapshotChanged;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
