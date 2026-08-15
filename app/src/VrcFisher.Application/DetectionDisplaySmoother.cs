using VrcFisher.Core;

namespace VrcFisher.Application;

public sealed class DetectionDisplaySmoother
{
    public const double PositionAlpha = 0.42;
    public const int MissingFrameTolerance = 2;
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMilliseconds(500);

    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);
    private long _lastFrameNumber = -1;
    private int _width;
    private int _height;
    private DateTimeOffset _capturedAt;

    public void Push(DetectionVisualizationFrame frame)
    {
        if (frame.FrameNumber <= _lastFrameNumber || frame.Width <= 0 || frame.Height <= 0)
            return;

        if (_width != 0 && (_width != frame.Width || _height != frame.Height))
            _tracks.Clear();

        _lastFrameNumber = frame.FrameNumber;
        _width = frame.Width;
        _height = frame.Height;
        _capturedAt = frame.CapturedAt;

        var measurements = frame.Detections
            .GroupBy(item => item.ClassName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.MaxBy(item => item.Confidence)!,
                StringComparer.Ordinal);

        foreach (var className in _tracks.Keys.ToArray())
        {
            if (measurements.ContainsKey(className)) continue;
            var track = _tracks[className] with { MissingFrames = _tracks[className].MissingFrames + 1 };
            if (track.MissingFrames > MissingFrameTolerance) _tracks.Remove(className);
            else _tracks[className] = track;
        }

        foreach (var measurement in measurements.Values)
        {
            if (!_tracks.TryGetValue(measurement.ClassName, out var previous)
                || IsLargeJump(previous.Visual.Box, measurement.Box, frame.Width, frame.Height))
            {
                _tracks[measurement.ClassName] = new Track(measurement, 0);
                continue;
            }

            _tracks[measurement.ClassName] = new Track(
                measurement with
                {
                    Box = Interpolate(previous.Visual.Box, measurement.Box, (float)PositionAlpha),
                    Confidence = Lerp(previous.Visual.Confidence, measurement.Confidence, (float)PositionAlpha)
                },
                0);
        }
    }

    public DetectionVisualizationFrame? GetCurrent(DateTimeOffset now)
    {
        if (_lastFrameNumber < 0 || now - _capturedAt > MaximumAge)
            return null;

        return new DetectionVisualizationFrame(
            _lastFrameNumber,
            _capturedAt,
            _width,
            _height,
            _tracks.Values
                .OrderBy(item => item.Visual.ClassName, StringComparer.Ordinal)
                .Select(item => item.Visual)
                .ToArray());
    }

    public void Reset()
    {
        _tracks.Clear();
        _lastFrameNumber = -1;
        _width = 0;
        _height = 0;
        _capturedAt = default;
    }

    private static bool IsLargeJump(BoundingBox previous, BoundingBox current, int width, int height)
    {
        var x = MathF.Abs(previous.CenterX - current.CenterX) / Math.Max(1, width);
        var y = MathF.Abs(previous.CenterY - current.CenterY) / Math.Max(1, height);
        return x > 0.15f || y > 0.15f;
    }

    private static BoundingBox Interpolate(BoundingBox previous, BoundingBox current, float alpha) => new(
        Lerp(previous.Left, current.Left, alpha),
        Lerp(previous.Top, current.Top, alpha),
        Lerp(previous.Right, current.Right, alpha),
        Lerp(previous.Bottom, current.Bottom, alpha));

    private static float Lerp(float previous, float current, float alpha) =>
        previous + (current - previous) * alpha;

    private sealed record Track(DetectionVisual Visual, int MissingFrames);
}
