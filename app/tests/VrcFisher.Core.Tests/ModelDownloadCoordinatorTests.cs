using VrcFisher.Application;
using VrcFisher.Core;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class ModelDownloadCoordinatorTests
{
    [Fact]
    public async Task Download_is_owned_by_the_coordinator_and_duplicate_start_is_rejected()
    {
        var models = new BlockingModelCatalog();
        await using var coordinator = new ModelDownloadCoordinator(models);
        var completed = new TaskCompletionSource<ModelDownloadSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, state) =>
        {
            if (state.Phase == ModelDownloadPhase.Completed)
                completed.TrySetResult(state);
        };

        Assert.True(coordinator.Start());
        await models.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(coordinator.Snapshot.IsActive);
        Assert.False(coordinator.Start());

        models.Release.SetResult();
        var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ModelDownloadPhase.Completed, result.Phase);
        Assert.Equal(1, models.DownloadCount);
    }

    private sealed class BlockingModelCatalog : IModelCatalog
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DownloadCount { get; private set; }
        public event EventHandler? StatusChanged;
        public bool IsReady => false;
        public bool AutomaticAllowed => false;
        public long InstalledSize => 0;
        public string Repository => "test/repository";
        public string? InstalledVersion => null;
        public string? LatestVersion => "1.0.0";
        public bool UpdateAvailable => false;
        public bool UpdateCheckSucceeded => true;
        public Uri SourceUri => new("https://example.invalid/");
        public IReadOnlyList<ModelStatus> GetStatus() => [];
        public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CheckForUpdatesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteModelsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<ModelManifest> DownloadLatestAsync(
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            DownloadCount++;
            Entered.TrySetResult();
            progress?.Report(new ModelDownloadProgress("locator.onnx", 1, 2, 0, 2));
            await Release.Task.WaitAsync(cancellationToken);
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return new ModelManifest(2, 1, "1.0.0", [], [], true);
        }
    }
}
