using VrcFisher.Application;
using VrcFisher.Core;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class AppOptionsTests
{
    [Fact]
    public void Normalize_rejects_invalid_language_and_inference_values()
    {
        var options = new AppOptions(
            Language: "fr-FR",
            ConfidenceThreshold: double.NaN,
            IoUThreshold: 2);

        var normalized = options.Normalize();

        Assert.Equal("zh-CN", normalized.Language);
        Assert.Equal(AppOptions.Default.ConfidenceThreshold, normalized.ConfidenceThreshold);
        Assert.Equal(0.99, normalized.IoUThreshold);
    }

    [Fact]
    public void Normalize_preserves_valid_inference_values()
    {
        var options = new AppOptions(
            Language: "en-US",
            ConfidenceThreshold: 0.5,
            IoUThreshold: 0.6,
            Device: ExecutionDevice.Cpu);

        var normalized = options.Normalize();

        Assert.Equal(options, normalized);
    }
}
