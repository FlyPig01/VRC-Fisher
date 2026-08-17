using VrcFisher.Core;
using VrcFisher.Infrastructure.Inference;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class MinigamePanelTrackerTests
{
    [Fact]
    public void Small_locator_jitter_keeps_the_stable_crop()
    {
        var tracker = new MinigamePanelTracker();
        var original = Detection(new BoundingBox(100, 100, 500, 700));
        tracker.UpdateFromLocator(original);

        var selected = tracker.UpdateFromLocator(Detection(new BoundingBox(104, 96, 504, 696)));

        Assert.Same(original, selected);
    }

    [Fact]
    public void Meaningful_panel_translation_replaces_the_crop()
    {
        var tracker = new MinigamePanelTracker();
        tracker.UpdateFromLocator(Detection(new BoundingBox(100, 100, 500, 700)));
        var moved = Detection(new BoundingBox(240, 180, 640, 780));

        var selected = tracker.UpdateFromLocator(moved);

        Assert.Same(moved, selected);
    }

    [Fact]
    public void Meaningful_panel_scaling_replaces_the_crop()
    {
        var tracker = new MinigamePanelTracker();
        tracker.UpdateFromLocator(Detection(new BoundingBox(100, 100, 500, 700)));
        var scaled = Detection(new BoundingBox(60, 40, 540, 760));

        var selected = tracker.UpdateFromLocator(scaled);

        Assert.Same(scaled, selected);
    }

    [Fact]
    public void Single_local_miss_keeps_crop_but_repeated_misses_force_relocation()
    {
        var tracker = new MinigamePanelTracker();
        var panel = Detection(new BoundingBox(100, 100, 500, 700));
        tracker.UpdateFromLocator(panel);

        tracker.ObserveLocalComponents(hasCatchZone: true, hasMovingTarget: false);
        Assert.Same(panel, tracker.Current);

        tracker.ObserveLocalComponents(hasCatchZone: false, hasMovingTarget: true);
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void Complete_local_detection_resets_the_miss_sequence()
    {
        var tracker = new MinigamePanelTracker();
        var panel = Detection(new BoundingBox(100, 100, 500, 700));
        tracker.UpdateFromLocator(panel);

        tracker.ObserveLocalComponents(hasCatchZone: false, hasMovingTarget: true);
        tracker.ObserveLocalComponents(hasCatchZone: true, hasMovingTarget: true);
        tracker.ObserveLocalComponents(hasCatchZone: true, hasMovingTarget: false);

        Assert.Same(panel, tracker.Current);
    }

    private static YoloDetection Detection(BoundingBox box) =>
        new("minigame_panel", 0.9f, box);
}
