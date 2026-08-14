using Microsoft.ML.OnnxRuntime.Tensors;
using VrcFisher.Core;
using VrcFisher.Infrastructure.Inference;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class YoloPostprocessorTests
{
    private static readonly string[] Classes = ["bite_indicator", "minigame_panel"];
    private static readonly LetterboxTransform Identity = new(1, 0, 0, 640, 640, 640, 640);

    [Fact]
    public void Decodes_transposed_xywh_output_and_applies_classwise_nms()
    {
        // [1, C, N], C = 4 box values + 2 class scores.
        var values = new float[]
        {
            100, 200, 300, // center x
            100, 200, 300, // center y
            40, 40, 40,     // width
            40, 40, 40,     // height
            .90f, .80f, .10f,
            .05f, .10f, .80f
        };
        var tensor = new DenseTensor<float>(values, new[] { 1, 6, 3 });

        var detections = YoloPostprocessor.Decode(tensor, Classes, .35f, .45f, Identity);

        Assert.Equal(3, detections.Count);
        Assert.Equal("bite_indicator", detections[0].ClassName);
        Assert.Equal(0.90f, detections[0].Confidence, 3);
        Assert.Equal(80f, detections[0].Box.Left, 2);
    }

    [Fact]
    public void Decodes_row_major_xywh_output()
    {
        // [1, N, C], C = 4 box values + 2 class scores.
        var tensor = new DenseTensor<float>(
            new float[] { 320, 320, 100, 80, .1f, .75f },
            new[] { 1, 1, 6 });

        var detections = YoloPostprocessor.Decode(tensor, Classes, .35f, .45f, Identity);

        var detection = Assert.Single(detections);
        Assert.Equal("minigame_panel", detection.ClassName);
        Assert.Equal(270f, detection.Box.Left, 2);
        Assert.Equal(370f, detection.Box.Right, 2);
    }

    [Fact]
    public void Decodes_exported_nms_rows_and_restores_letterbox_coordinates()
    {
        // [1, N, 6], x1/y1/x2/y2/confidence/class id in model coordinates.
        var tensor = new DenseTensor<float>(
            new float[] { 100, 200, 300, 400, .91f, 1 },
            new[] { 1, 1, 6 });
        var transform = new LetterboxTransform(2, 10, 20, 640, 640, 310, 300);

        var detections = YoloPostprocessor.Decode(
            tensor,
            Classes,
            .35f,
            .45f,
            transform,
            YoloOutputLayout.Nms);

        var detection = Assert.Single(detections);
        Assert.Equal("minigame_panel", detection.ClassName);
        Assert.Equal(45f, detection.Box.Left, 2);
        Assert.Equal(145f, detection.Box.Right, 2);
        Assert.Equal(90f, detection.Box.Top, 2);
        Assert.Equal(190f, detection.Box.Bottom, 2);
    }

    [Fact]
    public void Suppresses_overlapping_boxes_only_within_the_same_class()
    {
        // Three identical boxes: the lower-confidence class 0 box is removed,
        // while the class 1 box is retained by classwise NMS.
        var values = new float[]
        {
            100, 100, 100,
            100, 100, 100,
            40, 40, 40,
            40, 40, 40,
            .90f, .80f, .05f,
            .05f, .10f, .85f
        };
        var tensor = new DenseTensor<float>(values, new[] { 1, 6, 3 });

        var detections = YoloPostprocessor.Decode(tensor, Classes, .35f, .45f, Identity);

        Assert.Equal(2, detections.Count);
        Assert.Equal("bite_indicator", detections[0].ClassName);
        Assert.Equal(.90f, detections[0].Confidence, 3);
        Assert.Equal("minigame_panel", detections[1].ClassName);
        Assert.Equal(.85f, detections[1].Confidence, 3);
    }
}
