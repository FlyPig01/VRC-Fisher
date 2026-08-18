using VrcFisher.Core;
using VrcFisher.Infrastructure.Inference;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class OnnxRuntimeDetectorTests
{
    [Fact]
    public void Best_detection_applies_class_specific_confidence_threshold()
    {
        YoloDetection[] detections =
        [
            new("bite_indicator", 0.59f, new BoundingBox(0, 0, 10, 10)),
            new("minigame_panel", 0.40f, new BoundingBox(10, 10, 20, 20))
        ];

        var biteIndicator = OnnxRuntimeDetector.BestDetection(
            detections,
            "bite_indicator",
            minimumConfidence: 0.60f);
        var minigamePanel = OnnxRuntimeDetector.BestDetection(
            detections,
            "minigame_panel");

        Assert.Null(biteIndicator);
        Assert.NotNull(minigamePanel);
        Assert.Equal(0.40f, minigamePanel.Confidence);
    }

    [Fact]
    public void Best_detection_returns_highest_qualifying_confidence()
    {
        YoloDetection[] detections =
        [
            new("bite_indicator", 0.61f, new BoundingBox(0, 0, 10, 10)),
            new("bite_indicator", 0.85f, new BoundingBox(10, 10, 20, 20))
        ];

        var result = OnnxRuntimeDetector.BestDetection(
            detections,
            "bite_indicator",
            minimumConfidence: 0.60f);

        Assert.NotNull(result);
        Assert.Equal(0.85f, result.Confidence);
    }
}
