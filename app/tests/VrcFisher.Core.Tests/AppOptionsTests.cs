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
            IoUThreshold: 2,
            BiteFallbackSeconds: 100,
            LocatorIntervalMs: 10,
            HookingIntervalMs: 999,
            MinigameIntervalMs: 1,
            PanelRecheckIntervalMs: 5000);

        var normalized = options.Normalize();

        Assert.Equal("zh-CN", normalized.Language);
        Assert.Equal(AppOptions.Default.ConfidenceThreshold, normalized.ConfidenceThreshold);
        Assert.Equal(0.99, normalized.IoUThreshold);
        Assert.Equal(20, normalized.BiteFallbackSeconds);
        Assert.Equal(80, normalized.LocatorIntervalMs);
        Assert.Equal(250, normalized.HookingIntervalMs);
        Assert.Equal(33, normalized.MinigameIntervalMs);
        Assert.Equal(1000, normalized.PanelRecheckIntervalMs);
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
