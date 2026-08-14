using VrcFisher.Infrastructure.Runtime;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class PerformanceProfileStoreTests
{
    [Fact]
    public async Task Store_only_loads_exact_hardware_model_and_resolution_profile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "VrcFisherTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PerformanceProfileStore(directory);
            var identity = new PerformanceProfileIdentity(
                "CPUExecutionProvider",
                "cpu-a",
                "model-v1",
                1920,
                1080);
            var profile = new InferencePerformanceProfile(
                identity,
                100,
                150,
                40,
                500,
                65,
                120,
                25,
                DateTimeOffset.UtcNow);

            await store.SaveAsync(profile);

            Assert.Equal(profile, store.Load(identity));
            Assert.Null(store.Load(identity with { CaptureWidth = 2560 }));
            Assert.Null(store.Load(identity with { ModelVersion = "model-v2" }));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
