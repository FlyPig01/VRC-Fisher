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
}
