using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Infrastructure.Runtime;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class InferencePerformanceSchedulerTests
{
    [Fact]
    public void Adaptive_scheduler_uses_each_workload_budget()
    {
        var scheduler = new InferencePerformanceScheduler(AppOptions.Default, "DmlExecutionProvider");
        var now = DateTimeOffset.UtcNow;

        Feed(scheduler, InferenceWorkload.Locator, 65, 40, ref now);
        Evaluate(scheduler, InferenceWorkload.Locator, 65, ref now);
        Feed(scheduler, InferenceWorkload.LocatorAndMinigame, 120, 40, ref now);
        Evaluate(scheduler, InferenceWorkload.LocatorAndMinigame, 120, ref now);
        Feed(scheduler, InferenceWorkload.CachedMinigame, 25, 40, ref now);
        Evaluate(scheduler, InferenceWorkload.CachedMinigame, 25, ref now);

        var snapshot = scheduler.Snapshot;
        Assert.Equal(100, snapshot.LocatorIntervalMs);
        Assert.Equal(150, snapshot.HookingIntervalMs);
        Assert.Equal(40, snapshot.MinigameIntervalMs);
        Assert.Equal(500, snapshot.PanelRecheckIntervalMs);
        Assert.False(snapshot.PerformanceInsufficient);
    }

    [Fact]
    public void Adaptive_scheduler_ignores_warmup_and_waits_for_minimum_samples()
    {
        var scheduler = new InferencePerformanceScheduler(AppOptions.Default, "DmlExecutionProvider");
        var now = DateTimeOffset.UtcNow;

        Feed(scheduler, InferenceWorkload.Locator, 160, 39, ref now);
        Assert.Equal(80, scheduler.Snapshot.LocatorIntervalMs);

        now += InferencePerformanceScheduler.EvaluationInterval;
        scheduler.Record(InferenceWorkload.Locator, 160, 5, 0, now);
        Assert.Equal(250, scheduler.Snapshot.LocatorIntervalMs);
    }

    [Fact]
    public void Cached_minigame_frequency_stops_at_limit_and_reports_insufficient_performance()
    {
        var scheduler = new InferencePerformanceScheduler(AppOptions.Default, "DmlExecutionProvider");
        var now = DateTimeOffset.UtcNow;

        Feed(scheduler, InferenceWorkload.CachedMinigame, 60, 40, ref now);
        Evaluate(scheduler, InferenceWorkload.CachedMinigame, 60, ref now);

        Assert.Equal(67, scheduler.Snapshot.MinigameIntervalMs);
        Assert.True(scheduler.Snapshot.PerformanceInsufficient);
    }

    [Fact]
    public void Adaptive_scheduler_only_speeds_up_after_stable_hysteresis()
    {
        var scheduler = new InferencePerformanceScheduler(AppOptions.Default, "DmlExecutionProvider");
        scheduler.ApplyProfile(new InferencePerformanceProfile(
            new PerformanceProfileIdentity("DmlExecutionProvider", "gpu", "models", 1920, 1080),
            80,
            80,
            67,
            250,
            null,
            null,
            20,
            DateTimeOffset.UtcNow));
        var now = DateTimeOffset.UtcNow;

        Feed(scheduler, InferenceWorkload.CachedMinigame, 20, 45, ref now);
        var beforeHysteresis = scheduler.Snapshot.MinigameIntervalMs;
        Feed(scheduler, InferenceWorkload.CachedMinigame, 20, 125, ref now);

        Assert.Equal(67, beforeHysteresis);
        Assert.True(scheduler.Snapshot.MinigameIntervalMs < beforeHysteresis);
        Assert.True(scheduler.Snapshot.MinigameIntervalMs >= 33);
    }

    [Fact]
    public void Manual_scheduler_keeps_normalized_user_intervals()
    {
        var options = new AppOptions(
            AdaptiveInference: false,
            LocatorIntervalMs: 120,
            HookingIntervalMs: 140,
            MinigameIntervalMs: 45,
            PanelRecheckIntervalMs: 600).Normalize();
        var scheduler = new InferencePerformanceScheduler(options, "CPUExecutionProvider");

        Assert.Equal(120, scheduler.Snapshot.LocatorIntervalMs);
        Assert.Equal(140, scheduler.Snapshot.HookingIntervalMs);
        Assert.Equal(45, scheduler.Snapshot.MinigameIntervalMs);
        Assert.Equal(600, scheduler.Snapshot.PanelRecheckIntervalMs);
        Assert.False(scheduler.Snapshot.Adaptive);
        Assert.False(scheduler.Snapshot.IsCalibrating);
    }

    [Fact]
    public void Recording_hot_path_does_not_allocate_managed_memory()
    {
        var scheduler = new InferencePerformanceScheduler(AppOptions.Default, "DmlExecutionProvider");
        var now = DateTimeOffset.UtcNow;
        scheduler.Record(InferenceWorkload.Locator, 10, 5, 0, now);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
            scheduler.Record(InferenceWorkload.Locator, 10, 5, 0, now);

        Assert.Equal(allocatedBefore, GC.GetAllocatedBytesForCurrentThread());
    }

    private static void Feed(
        InferencePerformanceScheduler scheduler,
        InferenceWorkload workload,
        double latencyMs,
        int count,
        ref DateTimeOffset now)
    {
        for (var index = 0; index < count; index++)
        {
            now += TimeSpan.FromMilliseconds(250);
            scheduler.Record(workload, latencyMs, 5, 0, now);
        }
    }

    private static void Evaluate(
        InferencePerformanceScheduler scheduler,
        InferenceWorkload workload,
        double latencyMs,
        ref DateTimeOffset now)
    {
        now += InferencePerformanceScheduler.EvaluationInterval;
        scheduler.Record(workload, latencyMs, 5, 0, now);
    }
}
