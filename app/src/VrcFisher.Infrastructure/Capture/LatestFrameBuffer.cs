using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Capture;

public sealed class LatestFrameBuffer
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _available = new(0, 1);
    private CapturedFrameEventArgs? _latest;
    private long _dropped;

    public long DroppedCount => Interlocked.Read(ref _dropped);

    public void Clear(bool resetDropped = true)
    {
        lock (_sync)
        {
            _latest = null;
            _available.Wait(0);
            if (resetDropped) Interlocked.Exchange(ref _dropped, 0);
        }
    }

    public void Publish(CapturedFrameEventArgs frame)
    {
        lock (_sync)
        {
            if (_latest is not null) Interlocked.Increment(ref _dropped);
            _latest = frame;
            if (_available.CurrentCount == 0) _available.Release();
        }
    }

    public bool TryTake(out CapturedFrameEventArgs? frame)
    {
        lock (_sync)
        {
            frame = _latest;
            _latest = null;
            if (frame is not null) _available.Wait(0);
            return frame is not null;
        }
    }

    public ValueTask<CapturedFrameEventArgs?> WaitAsync(CancellationToken cancellationToken) =>
        WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);

    public async ValueTask<CapturedFrameEventArgs?> WaitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await _available.WaitAsync(timeout, cancellationToken)) return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        lock (_sync)
        {
            var value = _latest;
            _latest = null;
            _available.Wait(0);
            return value;
        }
    }
}
