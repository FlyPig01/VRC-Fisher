using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Capture;

/// <summary>
/// Capture boundary consumed by the runtime. The Windows Graphics Capture
/// frame-pool adapter is hosted by Desktop because it requires Windows App SDK
/// and a WinRT/Direct3D device. This assembly intentionally remains testable
/// without the GUI SDK; no synthetic frames are produced here.
/// </summary>
public sealed class WindowsGraphicsCaptureSource : IFrameSource
{
    private readonly LatestFrameBuffer _buffer = new();
    private long _sequence;
    private bool _running;

    public event EventHandler<CapturedFrameEventArgs>? FrameArrived;
    public bool IsConfigured { get; private set; }
    public string TargetName { get; private set; } = "未选择显示器或窗口";

    public void Configure(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName)) throw new ArgumentException("捕获目标不能为空", nameof(targetName));
        if (_running) throw new InvalidOperationException("捕获运行时不能更换目标");
        TargetName = targetName;
        IsConfigured = true;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("请先选择要捕获的显示器或窗口");
        _running = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _running = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Called by the Desktop WGC adapter after CPU readback.</summary>
    public void PublishCapturedFrame(ReadOnlyMemory<byte> bgraPixels, int width, int height)
    {
        if (!_running) return;
        var frame = new CapturedFrameEventArgs(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow,
            bgraPixels,
            width,
            height);
        _buffer.Publish(frame);
        FrameArrived?.Invoke(this, frame);
    }
}
