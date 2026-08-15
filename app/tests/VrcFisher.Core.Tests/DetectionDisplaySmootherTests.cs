using VrcFisher.Application;
using VrcFisher.Core;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class DetectionDisplaySmootherTests
{
    [Fact]
    public void Push_smooths_position_and_confidence_without_changing_the_source()
    {
        var smoother = new DetectionDisplaySmoother();
        var now = DateTimeOffset.UtcNow;
        smoother.Push(Frame(1, now, new DetectionVisual(
            "moving_target",
            0.5f,
            new BoundingBox(10, 10, 30, 30))));
        smoother.Push(Frame(2, now.AddMilliseconds(50), new DetectionVisual(
            "moving_target",
            0.9f,
            new BoundingBox(20, 20, 40, 40))));

        var current = Assert.Single(smoother.GetCurrent(now.AddMilliseconds(60))!.Detections);

        Assert.Equal(14.2f, current.Box.Left, 3);
        Assert.Equal(34.2f, current.Box.Right, 3);
        Assert.Equal(0.668f, current.Confidence, 3);
    }

    [Fact]
    public void Push_retains_two_missing_inference_results_then_removes_the_box()
    {
        var smoother = new DetectionDisplaySmoother();
        var now = DateTimeOffset.UtcNow;
        smoother.Push(Frame(1, now, Visual()));
        smoother.Push(Frame(2, now.AddMilliseconds(50)));
        smoother.Push(Frame(3, now.AddMilliseconds(100)));

        Assert.Single(smoother.GetCurrent(now.AddMilliseconds(110))!.Detections);

        smoother.Push(Frame(4, now.AddMilliseconds(150)));

        Assert.Empty(smoother.GetCurrent(now.AddMilliseconds(160))!.Detections);
    }

    [Fact]
    public void Push_snaps_large_screen_moves_instead_of_lagging_across_the_screen()
    {
        var smoother = new DetectionDisplaySmoother();
        var now = DateTimeOffset.UtcNow;
        smoother.Push(Frame(1, now, Visual()));
        var moved = new DetectionVisual(
            "bite_indicator",
            0.8f,
            new BoundingBox(70, 70, 90, 90));

        smoother.Push(Frame(2, now.AddMilliseconds(50), moved));

        Assert.Equal(moved, Assert.Single(smoother.GetCurrent(now.AddMilliseconds(60))!.Detections));
    }

    [Fact]
    public void GetCurrent_hides_stale_results()
    {
        var smoother = new DetectionDisplaySmoother();
        var now = DateTimeOffset.UtcNow;
        smoother.Push(Frame(1, now, Visual()));

        Assert.Null(smoother.GetCurrent(now + DetectionDisplaySmoother.MaximumAge + TimeSpan.FromMilliseconds(1)));
    }

    private static DetectionVisualizationFrame Frame(
        long number,
        DateTimeOffset capturedAt,
        params DetectionVisual[] detections) =>
        new(number, capturedAt, 100, 100, detections);

    private static DetectionVisual Visual() => new(
        "bite_indicator",
        0.6f,
        new BoundingBox(10, 10, 30, 30));
}
