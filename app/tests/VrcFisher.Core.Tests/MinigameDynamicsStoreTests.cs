using VrcFisher.Core;
using VrcFisher.Infrastructure.Runtime;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class MinigameDynamicsStoreTests
{
    [Fact]
    public async Task Saved_parameters_round_trip_inside_config_directory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vrc-fisher-dynamics-tests",
            Guid.NewGuid().ToString("N"));
        var config = Path.Combine(root, "config");
        try
        {
            var store = new MinigameDynamicsStore(config);

            await store.SaveAsync(new MinigameDynamicsParameters(-4.5, 12.25));
            var loaded = store.Load();

            Assert.Equal(-4.5, loaded.ReleaseAcceleration);
            Assert.Equal(12.25, loaded.PressAcceleration);
            Assert.True(File.Exists(Path.Combine(config, "minigame-dynamics.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Invalid_saved_signs_are_rejected()
    {
        var normalized = new MinigameDynamicsParameters(3, -8).Normalize();

        Assert.Null(normalized.ReleaseAcceleration);
        Assert.Null(normalized.PressAcceleration);
    }

    [Fact]
    public void Schema_one_parameters_are_migrated_to_upward_positive_coordinates()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vrc-fisher-dynamics-tests",
            Guid.NewGuid().ToString("N"));
        var config = Path.Combine(root, "config");
        try
        {
            Directory.CreateDirectory(config);
            File.WriteAllText(
                Path.Combine(config, "minigame-dynamics.json"),
                """
                {
                  "schemaVersion": 1,
                  "releaseAcceleration": 5.0,
                  "pressAcceleration": -16.0,
                  "updatedAt": "2026-08-17T00:00:00+08:00"
                }
                """);

            var loaded = new MinigameDynamicsStore(config).Load();

            Assert.Equal(-5, loaded.ReleaseAcceleration);
            Assert.Equal(16, loaded.PressAcceleration);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
