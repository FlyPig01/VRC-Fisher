using VrcFisher.Application;
using VrcFisher.Core;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class DetectionDisplayBufferTests
{
    [Fact]
    public void Push_exposes_the_latest_raw_detection_without_interpolation()
    {
        var buffer = new DetectionDisplayBuffer();
        var now = DateTimeOffset.UtcNow;
        var first = Visual(0.5f, new BoundingBox(10, 10, 30, 30));
        var latest = Visual(0.9f, new BoundingBox(20, 20, 40, 40));
        buffer.Push(Frame(1, now, first));
        buffer.Push(Frame(2, now.AddMilliseconds(50), latest));

        var current = Assert.Single(buffer.GetCurrent(now.AddMilliseconds(60))!.Detections);

        Assert.Equal(latest, current);
    }

    [Fact]
    public void Push_does_not_retain_a_detection_missing_from_the_latest_frame()
    {
        var buffer = new DetectionDisplayBuffer();
        var now = DateTimeOffset.UtcNow;
        buffer.Push(Frame(1, now, Visual()));
        buffer.Push(Frame(2, now.AddMilliseconds(50)));

        Assert.Empty(buffer.GetCurrent(now.AddMilliseconds(60))!.Detections);
    }

    [Fact]
    public void Push_ignores_out_of_order_frames()
    {
        var buffer = new DetectionDisplayBuffer();
        var now = DateTimeOffset.UtcNow;
        var latest = Visual(0.9f, new BoundingBox(20, 20, 40, 40));
        buffer.Push(Frame(2, now.AddMilliseconds(50), latest));
        buffer.Push(Frame(1, now, Visual()));

        Assert.Equal(latest, Assert.Single(buffer.GetCurrent(now.AddMilliseconds(60))!.Detections));
    }

    [Fact]
    public void GetCurrent_hides_results_older_than_one_hundred_fifty_ms()
    {
        var buffer = new DetectionDisplayBuffer();
        var now = DateTimeOffset.UtcNow;
        buffer.Push(Frame(1, now, Visual()));

        Assert.NotNull(buffer.GetCurrent(now + DetectionDisplayBuffer.MaximumAge));
        Assert.Null(buffer.GetCurrent(
            now + DetectionDisplayBuffer.MaximumAge + TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void Reset_clears_the_current_frame()
    {
        var buffer = new DetectionDisplayBuffer();
        var now = DateTimeOffset.UtcNow;
        buffer.Push(Frame(1, now, Visual()));

        buffer.Reset();

        Assert.Null(buffer.GetCurrent(now));
    }

    private static DetectionVisualizationFrame Frame(
        long number,
        DateTimeOffset capturedAt,
        params DetectionVisual[] detections) =>
        new(number, capturedAt, 100, 100, detections);

    private static DetectionVisual Visual(
        float confidence = 0.6f,
        BoundingBox? box = null) => new(
        "moving_target",
        confidence,
        box ?? new BoundingBox(10, 10, 30, 30));
}
