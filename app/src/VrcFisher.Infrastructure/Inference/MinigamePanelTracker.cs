using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Inference;

internal sealed class MinigamePanelTracker
{
    internal const int LocalMissLimit = 2;
    private const float RelocationIouThreshold = 0.75f;
    private const float RelocationCenterRatio = 0.10f;
    private const float RelocationSizeRatio = 0.18f;
    private int _consecutiveLocalMisses;
    private long _generation;

    public YoloDetection? Current { get; private set; }
    public long CurrentGeneration => Current is null ? 0 : _generation;

    public void Reset()
    {
        Current = null;
        _consecutiveLocalMisses = 0;
    }

    public YoloDetection UpdateFromLocator(YoloDetection detected)
    {
        if (Current is null || ShouldRelocate(Current.Box, detected.Box))
        {
            Current = detected;
            _generation++;
        }
        return Current;
    }

    public YoloDetection? RetainAfterSingleLocatorMiss() => Current;

    public void ObserveLocalComponents(bool hasCatchZone, bool hasMovingTarget)
    {
        if (hasCatchZone && hasMovingTarget)
        {
            _consecutiveLocalMisses = 0;
            return;
        }

        _consecutiveLocalMisses++;
        if (_consecutiveLocalMisses >= LocalMissLimit)
            Reset();
    }

    internal static bool ShouldRelocate(BoundingBox current, BoundingBox detected)
    {
        if (IntersectionOverUnion(current, detected) < RelocationIouThreshold)
            return true;

        var deltaX = detected.CenterX - current.CenterX;
        var deltaY = detected.CenterY - current.CenterY;
        var centerDistance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        var diagonal = MathF.Sqrt(current.Width * current.Width + current.Height * current.Height);
        if (centerDistance > MathF.Max(8f, diagonal * RelocationCenterRatio))
            return true;

        return RelativeChange(current.Width, detected.Width) > RelocationSizeRatio
               || RelativeChange(current.Height, detected.Height) > RelocationSizeRatio;
    }

    private static float IntersectionOverUnion(BoundingBox left, BoundingBox right)
    {
        var intersectionWidth = MathF.Max(0, MathF.Min(left.Right, right.Right) - MathF.Max(left.Left, right.Left));
        var intersectionHeight = MathF.Max(0, MathF.Min(left.Bottom, right.Bottom) - MathF.Max(left.Top, right.Top));
        var intersection = intersectionWidth * intersectionHeight;
        var union = left.Width * left.Height + right.Width * right.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private static float RelativeChange(float current, float detected) =>
        MathF.Abs(detected - current) / MathF.Max(1, current);
}
