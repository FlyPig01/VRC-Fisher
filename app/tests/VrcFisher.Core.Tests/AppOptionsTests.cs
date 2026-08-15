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
            Language: "vi-VN",
            WorkMode: (ApplicationMode)99,
            ConfidenceThreshold: double.NaN,
            IoUThreshold: 2,
            BiteFallbackSeconds: 100);

        var normalized = options.Normalize();

        Assert.Equal(UiLanguage.English, normalized.Language);
        Assert.Equal(ApplicationMode.Run, normalized.WorkMode);
        Assert.Equal(AppOptions.Default.ConfidenceThreshold, normalized.ConfidenceThreshold);
        Assert.Equal(0.99, normalized.IoUThreshold);
        Assert.Equal(30, normalized.BiteFallbackSeconds);
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

    [Fact]
    public void Normalize_restores_invalid_fallback_and_hotkey_defaults()
    {
        var normalized = new AppOptions(
            BiteFallbackSeconds: 0,
            ToggleHotkey: "A").Normalize();

        Assert.Equal(15, normalized.BiteFallbackSeconds);
        Assert.Equal("F8", normalized.ToggleHotkey);
    }

    [Fact]
    public void Options_store_migrates_legacy_disabled_fallback_to_fifteen_seconds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vrc-fisher-options-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "config"));
            File.WriteAllText(Path.Combine(root, "config", "user.json"), """
            {
              "language": "zh-CN",
              "biteFallbackEnabled": false,
              "biteFallbackSeconds": 5
            }
            """);

            var options = new OptionsStore(root).Load();

            Assert.Equal(15, options.BiteFallbackSeconds);
            Assert.Equal(AppOptions.CurrentSettingsVersion, options.SettingsVersion);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Language_catalog_contains_twenty_unique_native_language_names()
    {
        Assert.Equal(20, UiLanguage.Languages.Count);
        Assert.Equal(20, UiLanguage.Languages.Select(language => language.Code).Distinct().Count());
        Assert.Equal(20, UiLanguage.Languages.Select(language => language.NativeName).Distinct().Count());
        Assert.Equal("English", UiLanguage.Languages.Single(language => language.Code == "en-US").NativeName);
        Assert.Equal("日本語", UiLanguage.Languages.Single(language => language.Code == "ja-JP").NativeName);
    }

    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("ja-JP", "ja-JP")]
    [InlineData("fi-FI", "fi-FI")]
    [InlineData("system", "en-US")]
    [InlineData("vi-VN", "en-US")]
    public void Language_resolution_accepts_catalog_values_and_falls_back_to_english(
        string preference,
        string expected)
    {
        Assert.Equal(expected, UiLanguage.Resolve(preference));
    }
}
