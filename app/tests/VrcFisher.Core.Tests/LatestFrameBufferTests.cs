using VrcFisher.Infrastructure.Capture;
using VrcFisher.Core;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class LatestFrameBufferTests
{
    [Fact]
    public void Buffer_keeps_latest_frame_and_counts_replaced_frames()
    {
        var buffer = new LatestFrameBuffer();
        var first = new CapturedFrameEventArgs(1, DateTimeOffset.UtcNow, new byte[4], 1, 1);
        var second = new CapturedFrameEventArgs(2, DateTimeOffset.UtcNow, new byte[4], 1, 1);
        buffer.Publish(first);
        buffer.Publish(second);

        Assert.True(buffer.TryTake(out var actual));
        Assert.Equal(2, actual!.FrameNumber);
        Assert.Equal(1, buffer.DroppedCount);
    }

    [Fact]
    public async Task Wait_async_returns_null_when_cancelled()
    {
        var buffer = new LatestFrameBuffer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var frame = await buffer.WaitAsync(cancellation.Token);

        Assert.Null(frame);
    }

    [Fact]
    public async Task Wait_async_returns_null_after_timeout()
    {
        var buffer = new LatestFrameBuffer();

        var frame = await buffer.WaitAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);

        Assert.Null(frame);
    }
}
