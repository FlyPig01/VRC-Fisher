using VrcFisher.Core;

namespace VrcFisher.Application;

public sealed class DetectionDisplayBuffer
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMilliseconds(150);

    private DetectionVisualizationFrame? _current;

    public void Push(DetectionVisualizationFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
            return;
        if (_current is { } current && frame.FrameNumber <= current.FrameNumber)
            return;

        _current = frame;
    }

    public DetectionVisualizationFrame? GetCurrent(DateTimeOffset now)
    {
        if (_current is not { } current || now - current.CapturedAt > MaximumAge)
            return null;

        return current;
    }

    public void Reset() => _current = null;
}
