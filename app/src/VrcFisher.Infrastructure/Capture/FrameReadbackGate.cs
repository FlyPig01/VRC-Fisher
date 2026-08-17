using System.Diagnostics;

namespace VrcFisher.Infrastructure.Capture;

/// <summary>
/// Converts the push-based capture callback into one requested CPU readback.
/// The request remains armed until its due time and can only be claimed once.
/// </summary>
public sealed class FrameReadbackGate
{
    private const long NoRequest = long.MaxValue;
    private readonly Func<long> _getTimestamp;
    private readonly long _timestampFrequency;
    private long _dueTimestamp = NoRequest;

    public FrameReadbackGate() : this(Stopwatch.GetTimestamp, Stopwatch.Frequency) { }

    internal FrameReadbackGate(Func<long> getTimestamp, long timestampFrequency)
    {
        _getTimestamp = getTimestamp ?? throw new ArgumentNullException(nameof(getTimestamp));
        if (timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        _timestampFrequency = timestampFrequency;
    }

    public void Request(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay));

        var now = _getTimestamp();
        var requestedTicks = delay == TimeSpan.Zero
            ? 0L
            : checked((long)Math.Ceiling(delay.TotalSeconds * _timestampFrequency));
        var due = requestedTicks > long.MaxValue - now
            ? long.MaxValue - 1
            : now + requestedTicks;
        Interlocked.Exchange(ref _dueTimestamp, due);
    }

    public bool TryClaim()
    {
        while (true)
        {
            var due = Volatile.Read(ref _dueTimestamp);
            if (due == NoRequest || _getTimestamp() < due) return false;
            if (Interlocked.CompareExchange(ref _dueTimestamp, NoRequest, due) == due)
                return true;
        }
    }

    public void Cancel() => Interlocked.Exchange(ref _dueTimestamp, NoRequest);
}
