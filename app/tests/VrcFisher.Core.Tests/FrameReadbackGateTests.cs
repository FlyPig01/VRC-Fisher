using VrcFisher.Infrastructure.Capture;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class FrameReadbackGateTests
{
    [Fact]
    public void A_request_is_claimed_once_when_due()
    {
        long now = 100;
        var gate = new FrameReadbackGate(() => now, 1_000);

        gate.Request(TimeSpan.FromMilliseconds(100));

        Assert.False(gate.TryClaim());
        now = 200;
        Assert.True(gate.TryClaim());
        Assert.False(gate.TryClaim());
    }

    [Fact]
    public void A_new_request_replaces_an_older_request()
    {
        long now = 100;
        var gate = new FrameReadbackGate(() => now, 1_000);

        gate.Request(TimeSpan.FromMilliseconds(500));
        gate.Request(TimeSpan.Zero);

        Assert.True(gate.TryClaim());
    }

    [Fact]
    public void Cancel_removes_a_pending_request()
    {
        var gate = new FrameReadbackGate(() => 100, 1_000);

        gate.Request(TimeSpan.Zero);
        gate.Cancel();

        Assert.False(gate.TryClaim());
    }
}
