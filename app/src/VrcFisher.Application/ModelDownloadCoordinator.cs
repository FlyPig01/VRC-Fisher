using System.Diagnostics;
using VrcFisher.Core;

namespace VrcFisher.Application;

public enum ModelDownloadPhase
{
    Idle,
    Resolving,
    Downloading,
    Completed,
    Cancelled,
    Failed
}

public sealed record ModelDownloadSnapshot(
    ModelDownloadPhase Phase,
    ModelDownloadProgress? Progress = null,
    double BytesPerSecond = 0,
    string? Error = null)
{
    public static ModelDownloadSnapshot Idle { get; } = new(ModelDownloadPhase.Idle);

    public bool IsActive => Phase is ModelDownloadPhase.Resolving or ModelDownloadPhase.Downloading;
}

/// <summary>
/// Keeps one model download alive independently of any page instance.
/// </summary>
public sealed class ModelDownloadCoordinator(IModelCatalog models) : IAsyncDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private Task? _operation;
    private ModelDownloadSnapshot _snapshot = ModelDownloadSnapshot.Idle;

    public event EventHandler<ModelDownloadSnapshot>? StateChanged;

    public ModelDownloadSnapshot Snapshot
    {
        get { lock (_sync) return _snapshot; }
    }

    public bool Start()
    {
        CancellationTokenSource cancellation;
        lock (_sync)
        {
            if (_snapshot.IsActive) return false;
            cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            _snapshot = new ModelDownloadSnapshot(ModelDownloadPhase.Resolving);
            _operation = Task.Run(() => RunAsync(cancellation));
        }
        RaiseStateChanged();
        return true;
    }

    public void Cancel()
    {
        lock (_sync) _cancellation?.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        Task? operation;
        lock (_sync)
        {
            _cancellation?.Cancel();
            operation = _operation;
        }
        if (operation is not null)
        {
            try { await operation; }
            catch (OperationCanceledException) { }
        }
    }

    private async Task RunAsync(CancellationTokenSource cancellation)
    {
        try
        {
            var speed = new TransferSpeedMeter();
            var progress = new InlineProgress<ModelDownloadProgress>(value =>
                Update(new ModelDownloadSnapshot(
                    ModelDownloadPhase.Downloading,
                    value,
                    speed.Record(value.BytesDownloaded))));
            await models.DownloadLatestAsync(progress, cancellation.Token);
            Update(new ModelDownloadSnapshot(ModelDownloadPhase.Completed));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Update(new ModelDownloadSnapshot(ModelDownloadPhase.Cancelled));
        }
        catch (Exception error)
        {
            Update(new ModelDownloadSnapshot(
                ModelDownloadPhase.Failed,
                Error: error.GetBaseException().Message));
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                    _operation = null;
                }
            }
            cancellation.Dispose();
        }
    }

    private void Update(ModelDownloadSnapshot snapshot)
    {
        lock (_sync) _snapshot = snapshot;
        RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, Snapshot);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TransferSpeedMeter
    {
        private readonly object _sync = new();
        private long _lastTimestamp = Stopwatch.GetTimestamp();
        private long _lastBytes;
        private double _bytesPerSecond;

        public double Record(long bytes)
        {
            lock (_sync)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, now);
                if (bytes < _lastBytes)
                {
                    _lastBytes = bytes;
                    _lastTimestamp = now;
                    _bytesPerSecond = 0;
                    return 0;
                }
                if (elapsed < TimeSpan.FromMilliseconds(250)) return _bytesPerSecond;

                var current = (bytes - _lastBytes) / elapsed.TotalSeconds;
                _bytesPerSecond = _bytesPerSecond <= 0
                    ? current
                    : _bytesPerSecond * 0.7 + current * 0.3;
                _lastBytes = bytes;
                _lastTimestamp = now;
                return _bytesPerSecond;
            }
        }
    }
}
