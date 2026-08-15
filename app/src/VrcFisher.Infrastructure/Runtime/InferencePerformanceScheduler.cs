using VrcFisher.Application;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Runtime;

public sealed class InferencePerformanceScheduler
{
    public const int WarmupSamples = 10;
    public const int MinimumSamples = 30;
    public static readonly TimeSpan EvaluationInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan StableBeforeSpeedup = TimeSpan.FromSeconds(30);

    private readonly LatencyWindow _locator = new();
    private readonly LatencyWindow _locatorAndMinigame = new();
    private readonly LatencyWindow _cachedMinigame = new();
    private DateTimeOffset _lastEvaluationAt = DateTimeOffset.MinValue;
    private DateTimeOffset? _locatorStableSince;
    private DateTimeOffset? _hookingStableSince;
    private DateTimeOffset? _minigameStableSince;
    private DateTimeOffset? _panelStableSince;
    private bool _unstableSinceEvaluation;
    private bool _profileLoaded;
    private bool _performanceInsufficient;
    private int _locatorIntervalMs;
    private int _hookingIntervalMs;
    private int _minigameIntervalMs;
    private int _panelRecheckIntervalMs;
    private double? _locatorP95Ms;
    private double? _locatorAndMinigameP95Ms;
    private double? _cachedMinigameP95Ms;
    private double _lastFrameAgeMs;
    private long _inferenceOverruns;
    private long _droppedSinceEvaluation;
    private long _recentFramesDropped;

    public InferencePerformanceScheduler(AppOptions _, string provider)
    {
        var isCpu = provider.Contains("CPU", StringComparison.OrdinalIgnoreCase);
        _locatorIntervalMs = isCpu ? 100 : 80;
        _hookingIntervalMs = isCpu ? 150 : 80;
        _minigameIntervalMs = isCpu ? 40 : 33;
        _panelRecheckIntervalMs = isCpu ? 500 : 250;
    }

    public bool Adaptive => true;
    public TimeSpan PanelRecheckInterval => TimeSpan.FromMilliseconds(_panelRecheckIntervalMs);

    public TimeSpan GetInferenceInterval(FishingPhase phase) => phase switch
    {
        FishingPhase.Hooking => TimeSpan.FromMilliseconds(_hookingIntervalMs),
        FishingPhase.Minigame => TimeSpan.FromMilliseconds(_minigameIntervalMs),
        _ => TimeSpan.FromMilliseconds(_locatorIntervalMs)
    };

    public void ApplyProfile(InferencePerformanceProfile profile)
    {
        _locatorIntervalMs = Math.Clamp(profile.LocatorIntervalMs, 80, 250);
        _hookingIntervalMs = Math.Clamp(profile.HookingIntervalMs, 80, 250);
        _minigameIntervalMs = Math.Clamp(profile.MinigameIntervalMs, 33, 67);
        _panelRecheckIntervalMs = Math.Clamp(profile.PanelRecheckIntervalMs, 250, 1000);
        _locatorP95Ms = profile.LocatorP95Ms;
        _locatorAndMinigameP95Ms = profile.LocatorAndMinigameP95Ms;
        _cachedMinigameP95Ms = profile.CachedMinigameP95Ms;
        _profileLoaded = true;
    }

    public void Record(
        InferenceWorkload workload,
        double elapsedMs,
        double frameAgeMs,
        long framesDropped,
        DateTimeOffset now)
    {
        var window = GetWindow(workload);
        window.Add(elapsedMs);
        _lastFrameAgeMs = Math.Max(0, frameAgeMs);
        _droppedSinceEvaluation += Math.Max(0, framesDropped);

        var deadline = workload switch
        {
            InferenceWorkload.Locator => _locatorIntervalMs,
            InferenceWorkload.LocatorAndMinigame => _hookingIntervalMs,
            InferenceWorkload.CachedMinigame => _minigameIntervalMs,
            _ => _locatorIntervalMs
        };
        if (elapsedMs > deadline)
        {
            _inferenceOverruns++;
            _unstableSinceEvaluation = true;
        }
        if (frameAgeMs > 250)
            _unstableSinceEvaluation = true;

        EvaluateIfDue(now);
    }

    public void RecordFailure(DateTimeOffset now)
    {
        _unstableSinceEvaluation = true;
        EvaluateIfDue(now);
    }

    public InferencePerformanceSnapshot Snapshot => new(
        Adaptive: true,
        ProfileLoaded: _profileLoaded,
        IsCalibrating: _locatorP95Ms is null
             || _locatorAndMinigameP95Ms is null
             || _cachedMinigameP95Ms is null,
        PerformanceInsufficient: _performanceInsufficient,
        LocatorIntervalMs: _locatorIntervalMs,
        HookingIntervalMs: _hookingIntervalMs,
        MinigameIntervalMs: _minigameIntervalMs,
        PanelRecheckIntervalMs: _panelRecheckIntervalMs,
        LocatorP95Ms: _locatorP95Ms,
        LocatorAndMinigameP95Ms: _locatorAndMinigameP95Ms,
        CachedMinigameP95Ms: _cachedMinigameP95Ms,
        LastFrameAgeMs: _lastFrameAgeMs,
        InferenceOverruns: _inferenceOverruns,
        RecentFramesDropped: _recentFramesDropped);

    public InferencePerformanceProfile CreateProfile(PerformanceProfileIdentity identity) => new(
        identity,
        _locatorIntervalMs,
        _hookingIntervalMs,
        _minigameIntervalMs,
        _panelRecheckIntervalMs,
        _locatorP95Ms,
        _locatorAndMinigameP95Ms,
        _cachedMinigameP95Ms,
        DateTimeOffset.UtcNow);

    private void EvaluateIfDue(DateTimeOffset now)
    {
        if (_lastEvaluationAt != DateTimeOffset.MinValue
            && now - _lastEvaluationAt < EvaluationInterval)
        {
            return;
        }

        _lastEvaluationAt = now;
        _recentFramesDropped = _droppedSinceEvaluation;
        _droppedSinceEvaluation = 0;
        var stable = !_unstableSinceEvaluation;
        _unstableSinceEvaluation = false;

        UpdateP95(_locator, ref _locatorP95Ms);
        UpdateP95(_locatorAndMinigame, ref _locatorAndMinigameP95Ms);
        UpdateP95(_cachedMinigame, ref _cachedMinigameP95Ms);
        if (_locator.SampleCount >= MinimumSamples && _locatorP95Ms is { } locatorP95)
        {
            var target = SnapAndClamp(locatorP95 / 0.65, 80, 250, 10);
            Tune(ref _locatorIntervalMs, target, 10, stable, now, ref _locatorStableSince);
        }
        if (_locatorAndMinigame.SampleCount >= MinimumSamples
            && _locatorAndMinigameP95Ms is { } combinedP95)
        {
            var hookingTarget = SnapAndClamp(combinedP95 / 0.80, 80, 250, 10);
            var panelTarget = SnapAndClamp(combinedP95 * 4, 250, 1000, 50);
            Tune(ref _hookingIntervalMs, hookingTarget, 10, stable, now, ref _hookingStableSince);
            Tune(ref _panelRecheckIntervalMs, panelTarget, 50, stable, now, ref _panelStableSince);
        }
        if (_cachedMinigame.SampleCount >= MinimumSamples
            && _cachedMinigameP95Ms is { } cachedP95)
        {
            var required = cachedP95 / 0.65;
            _performanceInsufficient = required > 67;
            var target = SnapAndClamp(required, 33, 67, 5);
            Tune(ref _minigameIntervalMs, target, 5, stable, now, ref _minigameStableSince);
        }
    }

    private static void UpdateP95(LatencyWindow window, ref double? destination)
    {
        if (window.SampleCount >= MinimumSamples) destination = window.P95();
    }

    private static void Tune(
        ref int current,
        int target,
        int step,
        bool stable,
        DateTimeOffset now,
        ref DateTimeOffset? stableSince)
    {
        if (target > current)
        {
            current = target;
            stableSince = null;
            return;
        }
        if (target == current || !stable)
        {
            stableSince = stable ? stableSince : null;
            return;
        }

        stableSince ??= now;
        if (now - stableSince < StableBeforeSpeedup) return;
        current = Math.Max(target, current - step);
        stableSince = now;
    }

    private LatencyWindow GetWindow(InferenceWorkload workload) => workload switch
    {
        InferenceWorkload.Locator => _locator,
        InferenceWorkload.LocatorAndMinigame => _locatorAndMinigame,
        InferenceWorkload.CachedMinigame => _cachedMinigame,
        _ => throw new ArgumentOutOfRangeException(nameof(workload))
    };

    private static int SnapAndClamp(double value, int minimum, int maximum, int quantum)
    {
        var snapped = (int)Math.Round(value / quantum, MidpointRounding.AwayFromZero) * quantum;
        return Math.Clamp(snapped, minimum, maximum);
    }

    private sealed class LatencyWindow
    {
        private const int Capacity = 120;
        private readonly double[] _samples = new double[Capacity];
        private int _warmupRemaining = WarmupSamples;
        private int _next;

        public int SampleCount { get; private set; }

        public void Add(double elapsedMs)
        {
            if (_warmupRemaining > 0)
            {
                _warmupRemaining--;
                return;
            }
            _samples[_next] = Math.Max(0, elapsedMs);
            _next = (_next + 1) % Capacity;
            if (SampleCount < Capacity) SampleCount++;
        }

        public double P95()
        {
            if (SampleCount == 0) throw new InvalidOperationException("No latency samples are available.");
            Span<double> sorted = stackalloc double[Capacity];
            _samples.AsSpan(0, SampleCount).CopyTo(sorted);
            sorted[..SampleCount].Sort();
            var index = Math.Clamp((int)Math.Ceiling(SampleCount * 0.95) - 1, 0, SampleCount - 1);
            return sorted[index];
        }
    }
}
