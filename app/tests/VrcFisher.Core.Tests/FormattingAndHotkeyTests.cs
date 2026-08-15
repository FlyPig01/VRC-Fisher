using VrcFisher.Application;
using VrcFisher.Infrastructure.Runtime;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class FormattingAndHotkeyTests
{
    [Theory]
    [InlineData(0, 0, "B")]
    [InlineData(512, 1023, "B")]
    [InlineData(1024, 4096, "KB")]
    [InlineData(1048576, 20971520, "MB")]
    [InlineData(1073741824, 2147483648, "GB")]
    public void Download_progress_uses_one_stable_international_unit(long downloaded, long total, string unit)
    {
        var value = DataSizeFormatter.FormatProgress(downloaded, total);

        Assert.EndsWith(unit, value);
        Assert.Contains(" / ", value);
        Assert.DoesNotContain("bytes", value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Download_progress_supports_unknown_total()
    {
        Assert.Contains(" / ? ", DataSizeFormatter.FormatProgress(2048, 0));
    }

    [Theory]
    [InlineData("f8", "F8")]
    [InlineData("shift+ctrl+a", "Ctrl+Shift+A")]
    [InlineData("alt+7", "Alt+7")]
    public void Hotkey_gestures_are_normalized(string input, string expected)
    {
        Assert.True(HotkeyGestureRules.TryNormalize(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Ctrl")]
    [InlineData("Alt+F4")]
    [InlineData("Ctrl+Mouse1")]
    public void Unsupported_or_reserved_hotkeys_are_rejected(string input)
    {
        Assert.False(HotkeyGestureRules.TryNormalize(input, out _));
    }

    [Fact]
    public async Task Windows_hardware_provider_returns_a_structured_snapshot()
    {
        var snapshot = await new WindowsHardwareInfoProvider().ReadAsync(CancellationToken.None);

        Assert.True(snapshot.IsX64);
        Assert.True(snapshot.LogicalProcessors > 0);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.CpuName));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.WindowsVersion));
        Assert.True(snapshot.TotalMemoryBytes > 0);
        Assert.All(snapshot.GraphicsAdapters, adapter =>
        {
            Assert.True(adapter.Index >= 0);
            Assert.False(string.IsNullOrWhiteSpace(adapter.Name));
            Assert.True(adapter.DedicatedMemoryBytes >= 0);
        });
    }
}
