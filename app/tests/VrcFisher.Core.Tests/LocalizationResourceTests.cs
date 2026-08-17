using System.Xml.Linq;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class LocalizationResourceTests
{
    [Fact]
    public void Every_language_contains_the_same_nonempty_resource_keys()
    {
        var stringsRoot = FindStringsRoot();
        var files = Directory.GetFiles(stringsRoot, "Resources.resw", SearchOption.AllDirectories);
        Assert.Equal(20, files.Length);

        var expected = ReadResources(Path.Combine(stringsRoot, "en-US", "Resources.resw"));
        foreach (var file in files)
        {
            var actual = ReadResources(file);
            Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
            Assert.All(actual, item => Assert.False(string.IsNullOrWhiteSpace(item.Value), $"{file}: {item.Key}"));
        }
    }

    [Fact]
    public void Overlay_stage_resources_exist_in_all_twenty_languages()
    {
        var required = new[]
        {
            "OverlayStageCasting",
            "OverlayStageWaitingForBite",
            "OverlayStageFightingFish",
            "OverlayStageReeling",
            "OverlayStopKeyHint"
        };
        var files = Directory.GetFiles(
            FindStringsRoot(),
            "Resources.resw",
            SearchOption.AllDirectories);

        Assert.Equal(20, files.Length);
        foreach (var file in files)
        {
            var resources = ReadResources(file);
            Assert.All(required, key => Assert.True(
                resources.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value),
                $"{file}: missing {key}"));
        }
    }

    private static IReadOnlyDictionary<string, string> ReadResources(string path)
    {
        var entries = XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(item => new
            {
                Name = (string?)item.Attribute("name"),
                Value = (string?)item.Element("value")
            })
            .ToArray();
        Assert.DoesNotContain(entries, item => string.IsNullOrWhiteSpace(item.Name));
        Assert.Equal(entries.Length, entries.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count());
        return entries.ToDictionary(item => item.Name!, item => item.Value ?? string.Empty, StringComparer.Ordinal);
    }

    private static string FindStringsRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "VrcFisher.Desktop",
                "Strings");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("Could not locate VrcFisher.Desktop localization resources.");
    }
}
