using Microsoft.ML.OnnxRuntime.Tensors;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Inference;

public sealed record YoloDetection(string ClassName, float Confidence, BoundingBox Box);

public enum YoloOutputLayout
{
    Raw,
    Nms
}

public readonly record struct LetterboxTransform(
    float Scale,
    int OffsetX,
    int OffsetY,
    int InputWidth,
    int InputHeight,
    int SourceWidth,
    int SourceHeight)
{
    public BoundingBox ToSource(BoundingBox box)
    {
        var left = (box.Left - OffsetX) / Scale;
        var top = (box.Top - OffsetY) / Scale;
        var right = (box.Right - OffsetX) / Scale;
        var bottom = (box.Bottom - OffsetY) / Scale;
        return new BoundingBox(
            Math.Clamp(left, 0, SourceWidth),
            Math.Clamp(top, 0, SourceHeight),
            Math.Clamp(right, 0, SourceWidth),
            Math.Clamp(bottom, 0, SourceHeight));
    }
}

/// <summary>
/// Decodes the output layouts produced by Ultralytics exports without OpenCV.
/// The model contract is deliberately explicit so an unexpected tensor fails
/// closed instead of being interpreted as a mouse decision.
/// </summary>
public static class YoloPostprocessor
{
    public static IReadOnlyList<YoloDetection> Decode(
        Tensor<float> output,
        IReadOnlyList<string> classNames,
        float confidenceThreshold,
        float iouThreshold,
        LetterboxTransform transform,
        YoloOutputLayout layout = YoloOutputLayout.Raw)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (classNames.Count == 0) throw new ArgumentException("至少需要一个类别", nameof(classNames));
        if (confidenceThreshold is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidenceThreshold));
        if (iouThreshold is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(iouThreshold));

        var dimensions = output.Dimensions.ToArray();
        while (dimensions.Length > 2 && dimensions[0] == 1)
            dimensions = dimensions[1..];
        if (dimensions.Length != 2)
            throw new InvalidDataException($"不支持的 YOLO 输出形状：[{string.Join(",", output.Dimensions.ToArray())}]");

        var first = dimensions[0];
        var second = dimensions[1];
        var expected4 = classNames.Count + 4;
        var expected5 = classNames.Count + 5;
        var transposed = first == 6 || first == expected4 || first == expected5 ||
                         (first <= 128 && first < second && second > 128);
        var rowCount = transposed ? second : first;
        var valueCount = transposed ? first : second;
        if (valueCount != 6 && valueCount != expected4 && valueCount != expected5)
            throw new InvalidDataException($"不支持的 YOLO 输出形状：[{string.Join(",", output.Dimensions.ToArray())}]");
        if (rowCount == 0) return [];

        if (output is DenseTensor<float> dense)
            return DecodeValues(dense.Buffer.Span, rowCount, valueCount, transposed, classNames,
                confidenceThreshold, iouThreshold, transform, layout);

        var values = output.ToArray();
        return DecodeValues(values, rowCount, valueCount, transposed, classNames,
            confidenceThreshold, iouThreshold, transform, layout);
    }

    private static IReadOnlyList<YoloDetection> DecodeValues(
        ReadOnlySpan<float> values,
        int rowCount,
        int valueCount,
        bool transposed,
        IReadOnlyList<string> classNames,
        float confidenceThreshold,
        float iouThreshold,
        LetterboxTransform transform,
        YoloOutputLayout layout)
    {
        var candidates = new List<Candidate>(Math.Min(rowCount, 64));
        for (var row = 0; row < rowCount; row++)
        {
            if (layout == YoloOutputLayout.Nms)
            {
                if (valueCount != 6)
                    throw new InvalidDataException($"NMS 输出每行必须有 6 个值，实际为 {valueCount}");
                var exportedClassId = (int)MathF.Round(ValueAt(values, row, 5, rowCount, valueCount, transposed));
                AddCandidate(
                    candidates,
                    ValueAt(values, row, 0, rowCount, valueCount, transposed),
                    ValueAt(values, row, 1, rowCount, valueCount, transposed),
                    ValueAt(values, row, 2, rowCount, valueCount, transposed),
                    ValueAt(values, row, 3, rowCount, valueCount, transposed),
                    ValueAt(values, row, 4, rowCount, valueCount, transposed),
                    exportedClassId,
                    classNames, confidenceThreshold, transform);
                continue;
            }

            var hasObjectness = valueCount == classNames.Count + 5;
            var expected = classNames.Count + (hasObjectness ? 5 : 4);
            if (valueCount != expected)
                throw new InvalidDataException($"YOLO 输出每行有 {valueCount} 个值，期望 {classNames.Count + 4} 或 {classNames.Count + 5}");

            var classStart = hasObjectness ? 5 : 4;
            var scoreClassId = 0;
            var classScore = float.NegativeInfinity;
            for (var index = classStart; index < valueCount; index++)
            {
                var score = ValueAt(values, row, index, rowCount, valueCount, transposed);
                if (score > classScore)
                {
                    classScore = score;
                    scoreClassId = index - classStart;
                }
            }
            var confidence = hasObjectness
                ? ValueAt(values, row, 4, rowCount, valueCount, transposed) * classScore
                : classScore;
            AddCandidate(
                candidates,
                ValueAt(values, row, 0, rowCount, valueCount, transposed),
                ValueAt(values, row, 1, rowCount, valueCount, transposed),
                ValueAt(values, row, 2, rowCount, valueCount, transposed),
                ValueAt(values, row, 3, rowCount, valueCount, transposed),
                confidence,
                scoreClassId,
                classNames, confidenceThreshold, transform, xywh: true);
        }

        var kept = ClasswiseNms(candidates, iouThreshold);
        var detections = new YoloDetection[kept.Count];
        for (var index = 0; index < kept.Count; index++)
        {
            var candidate = kept[index];
            detections[index] = new YoloDetection(
                classNames[candidate.ClassId],
                candidate.Confidence,
                candidate.Box);
        }
        return detections;
    }

    private static float ValueAt(
        ReadOnlySpan<float> values,
        int row,
        int column,
        int rowCount,
        int valueCount,
        bool transposed) => values[transposed ? column * rowCount + row : row * valueCount + column];

    private static void AddCandidate(
        List<Candidate> candidates,
        float x,
        float y,
        float widthOrRight,
        float heightOrBottom,
        float confidence,
        int classId,
        IReadOnlyList<string> classNames,
        float threshold,
        LetterboxTransform transform,
        bool xywh = false)
    {
        if (!float.IsFinite(confidence) || confidence < threshold || classId < 0 || classId >= classNames.Count)
            return;

        var scaleX = transform.InputWidth;
        var scaleY = transform.InputHeight;
        var coordinatesAreNormalized = MathF.Abs(x) <= 1.5f && MathF.Abs(y) <= 1.5f &&
                                       MathF.Abs(widthOrRight) <= 1.5f && MathF.Abs(heightOrBottom) <= 1.5f;
        if (coordinatesAreNormalized)
        {
            x *= scaleX;
            y *= scaleY;
            widthOrRight *= scaleX;
            heightOrBottom *= scaleY;
        }

        var box = xywh
            ? new BoundingBox(x - widthOrRight / 2, y - heightOrBottom / 2,
                x + widthOrRight / 2, y + heightOrBottom / 2)
            : new BoundingBox(x, y, widthOrRight, heightOrBottom);
        box = transform.ToSource(box);
        if (box.Width <= 0 || box.Height <= 0) return;
        candidates.Add(new Candidate(classId, confidence, box));
    }

    private static IReadOnlyList<Candidate> ClasswiseNms(List<Candidate> candidates, float threshold)
    {
        candidates.Sort(static (left, right) => right.Confidence.CompareTo(left.Confidence));
        var kept = new List<Candidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var suppressed = false;
            foreach (var accepted in kept)
            {
                if (candidate.ClassId == accepted.ClassId &&
                    IntersectionOverUnion(candidate.Box, accepted.Box) > threshold)
                {
                    suppressed = true;
                    break;
                }
            }
            if (!suppressed) kept.Add(candidate);
        }
        return kept;
    }

    private static float IntersectionOverUnion(BoundingBox left, BoundingBox right)
    {
        var intersection = new BoundingBox(
            MathF.Max(left.Left, right.Left), MathF.Max(left.Top, right.Top),
            MathF.Min(left.Right, right.Right), MathF.Min(left.Bottom, right.Bottom));
        var intersectionArea = intersection.Width * intersection.Height;
        var union = left.Width * left.Height + right.Width * right.Height - intersectionArea;
        return union <= 0 ? 0 : intersectionArea / union;
    }

    private readonly record struct Candidate(int ClassId, float Confidence, BoundingBox Box);
}
