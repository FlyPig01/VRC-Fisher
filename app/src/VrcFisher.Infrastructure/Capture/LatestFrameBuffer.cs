using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Capture;

public sealed class LatestFrameBuffer
{
    private readonly object _sync = new();
    private CapturedFrameEventArgs? _latest;
    private long _dropped;

    public long DroppedCount => Interlocked.Read(ref _dropped);

    public void Publish(CapturedFrameEventArgs frame)
    {
        lock (_sync)
        {
            if (_latest is not null) Interlocked.Increment(ref _dropped);
            _latest = frame;
            Monitor.PulseAll(_sync);
        }
    }

    public bool TryTake(out CapturedFrameEventArgs? frame)
    {
        lock (_sync)
        {
            frame = _latest;
            _latest = null;
            return frame is not null;
        }
    }

    public async ValueTask<CapturedFrameEventArgs?> WaitAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                if (_latest is not null)
                {
                    var value = _latest;
                    _latest = null;
                    return value;
                }
            }
            await Task.Delay(1, cancellationToken);
        }
        return null;
    }
}
