using Microsoft.ML.OnnxRuntime.Tensors;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Inference;

public sealed record YoloDetection(string ClassName, float Confidence, BoundingBox Box);

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
        LetterboxTransform transform)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (classNames.Count == 0) throw new ArgumentException("至少需要一个类别", nameof(classNames));
        if (confidenceThreshold is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidenceThreshold));
        if (iouThreshold is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(iouThreshold));

        var rows = ToRows(output, classNames.Count);
        if (rows.Count == 0) return [];

        var candidates = new List<Candidate>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Length == 6)
            {
                var exportedClassId = (int)MathF.Round(row[5]);
                AddCandidate(candidates, row[0], row[1], row[2], row[3], row[4], exportedClassId,
                    classNames, confidenceThreshold, transform);
                continue;
            }

            var hasObjectness = row.Length == classNames.Count + 5;
            var expected = classNames.Count + (hasObjectness ? 5 : 4);
            if (row.Length != expected)
                throw new InvalidDataException($"YOLO 输出每行有 {row.Length} 个值，期望 {classNames.Count + 4} 或 {classNames.Count + 5}");

            var classStart = hasObjectness ? 5 : 4;
            var scoreClassId = 0;
            var classScore = float.NegativeInfinity;
            for (var index = classStart; index < row.Length; index++)
            {
                if (row[index] > classScore)
                {
                    classScore = row[index];
                    scoreClassId = index - classStart;
                }
            }
            var confidence = hasObjectness ? row[4] * classScore : classScore;
            AddCandidate(candidates, row[0], row[1], row[2], row[3], confidence, scoreClassId,
                classNames, confidenceThreshold, transform, xywh: true);
        }

        var kept = ClasswiseNms(candidates, iouThreshold);
        return kept
            .OrderByDescending(item => item.Confidence)
            .Select(item => new YoloDetection(item.ClassName, item.Confidence, item.Box))
            .ToArray();
    }

    private static List<float[]> ToRows(Tensor<float> tensor, int classCount)
    {
        var dimensions = tensor.Dimensions.ToArray();
        while (dimensions.Length > 2 && dimensions[0] == 1)
            dimensions = dimensions[1..];
        if (dimensions.Length != 2)
            throw new InvalidDataException($"不支持的 YOLO 输出形状：[{string.Join(",", tensor.Dimensions.ToArray())}]");

        var first = dimensions[0];
        var second = dimensions[1];
        var expected4 = classCount + 4;
        var expected5 = classCount + 5;
        var transposed = first == 6 || first == expected4 || first == expected5 ||
                         (first <= 128 && first < second && second > 128);
        var rowCount = transposed ? second : first;
        var valueCount = transposed ? first : second;
        if (valueCount != 6 && valueCount != expected4 && valueCount != expected5)
            throw new InvalidDataException($"不支持的 YOLO 输出形状：[{string.Join(",", tensor.Dimensions.ToArray())}]");

        var rows = new List<float[]>(rowCount);
        var flat = tensor.ToArray();
        for (var row = 0; row < rowCount; row++)
        {
            var values = new float[valueCount];
            // Reading flat storage avoids assumptions about rank after the
            // optional batch dimension has been squeezed.
            for (var column = 0; column < valueCount; column++)
            {
                var sourceIndex = transposed ? column * rowCount + row : row * valueCount + column;
                values[column] = flat[sourceIndex];
            }
            rows.Add(values);
        }
        return rows;
    }

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
        candidates.Add(new Candidate(classNames[classId], confidence, box));
    }

    private static IReadOnlyList<Candidate> ClasswiseNms(List<Candidate> candidates, float threshold)
    {
        var kept = new List<Candidate>();
        foreach (var group in candidates.GroupBy(item => item.ClassName, StringComparer.Ordinal))
        {
            var pending = group.OrderByDescending(item => item.Confidence).ToList();
            while (pending.Count > 0)
            {
                var current = pending[0];
                kept.Add(current);
                pending.RemoveAt(0);
                pending.RemoveAll(item => IntersectionOverUnion(current.Box, item.Box) > threshold);
            }
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

    private sealed record Candidate(string ClassName, float Confidence, BoundingBox Box);
}
