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
    Release
}

public enum ExecutionDevice
{
    Auto,
    Cpu,
    Gpu
}

public enum RuntimeMessageCode
{
    ModelsUnavailable,
    ModelsRequired,
    AutomaticNotAllowed,
    AutomaticStarted,
    ObservationStarted,
    DetectionStopped,
    Stopped,
    CaptureStopped,
    FrameStale,
    TargetNotForeground,
    OutputContractUnverified,
    InferenceFailed,
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
    BoundingBox? FishingUi = null,
    BoundingBox? Prompt = null,
    BoundingBox? Success = null,
    BoundingBox? Failure = null,
    BoundingBox? Rail = null,
    BoundingBox? ControlBar = null,
    BoundingBox? Target = null,
    BoundingBox? ProgressBar = null,
    float? TargetYNorm = null,
    float? ControlTopNorm = null,
    float? ControlBottomNorm = null,
    float? ProgressNorm = null)
{
    public bool HasFishingUi => FishingUi is not null;
    public bool HasPrompt => Prompt is not null;
    public bool HasSuccess => Success is not null;
    public bool HasFailure => Failure is not null;
}

public readonly record struct StateDecision(
    FishingPhase Phase,
    InputAction Action,
    string Reason,
    int Cycle);

public sealed record StateMachineOptions(
    int PromptConfirmFrames = 3,
    int UiConfirmFrames = 3,
    int UiLostFrames = 5,
    int SuccessConfirmFrames = 3,
    int FailureConfirmFrames = 3,
    TimeSpan CastSettle = default,
    TimeSpan BiteTimeout = default,
    TimeSpan HookToUiMinimum = default,
    TimeSpan PromptToUiTimeout = default,
    TimeSpan MinigameTimeout = default,
    TimeSpan LootTimeout = default,
    TimeSpan RecoveryDelay = default,
    TimeSpan CycleDelay = default,
    float VerticalDeadband = 0.04f)
{
    public static StateMachineOptions Default => new(
        CastSettle: TimeSpan.FromMilliseconds(450),
        BiteTimeout: TimeSpan.FromSeconds(20),
        HookToUiMinimum: TimeSpan.FromMilliseconds(120),
        PromptToUiTimeout: TimeSpan.FromSeconds(3),
        MinigameTimeout: TimeSpan.FromSeconds(30),
        LootTimeout: TimeSpan.FromSeconds(5),
        RecoveryDelay: TimeSpan.FromSeconds(1),
        CycleDelay: TimeSpan.FromSeconds(1));
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
    void Click();
    void PressLeft();
    void ReleaseLeft();
    void ReleaseAll();
}

public interface IFrameSource : IAsyncDisposable
{
    event EventHandler<CapturedFrameEventArgs>? FrameArrived;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public sealed class CapturedFrameEventArgs(long frameNumber, DateTimeOffset capturedAt, ReadOnlyMemory<byte> bgraPixels, int width, int height) : EventArgs
{
    public long FrameNumber { get; } = frameNumber;
    public DateTimeOffset CapturedAt { get; } = capturedAt;
    public ReadOnlyMemory<byte> BgraPixels { get; } = bgraPixels;
    public int Width { get; } = width;
    public int Height { get; } = height;
}

public interface IDetector
{
    string Provider { get; }
    bool IsReady { get; }
    DetectionObservation Detect(CapturedFrameEventArgs frame);
}

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
    FishingPhase Phase,
    bool IsObserving,
    bool IsAutomatic,
    bool ModelsReady,
    string Provider,
    long FramesCaptured,
    long FramesDropped,
    RuntimeStatus Status,
    DateTimeOffset UpdatedAt);

public sealed record RuntimeStatus(RuntimeMessageCode Code, string? Detail = null);

public sealed record DetectionRuntimeMetrics(
    long FramesCaptured,
    long FramesDropped,
    FishingPhase Phase,
    RuntimeStatus Status,
    DateTimeOffset UpdatedAt);

public interface IRuntimeController
{
    RuntimeSnapshot Snapshot { get; }
    event EventHandler<RuntimeSnapshot>? SnapshotChanged;
    Task StartObservationAsync(bool automatic, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
