using System.Diagnostics;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Capture;

/// <summary>
/// Testable frame source boundary. The Desktop WGC adapter performs the
/// WinRT/Direct3D readback and publishes BGRA frames through this class.
/// </summary>
public sealed class WindowsGraphicsCaptureSource : IFrameSource
{
    private long _sequence;
    private bool _running;

    public event EventHandler<CapturedFrameEventArgs>? FrameArrived;
    public event EventHandler<FrameSourceFailedEventArgs>? CaptureFailed;
    public bool IsConfigured { get; private set; }
    public string TargetName { get; private set; } = "VRChat";

    public void Configure(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName)) throw new ArgumentException("捕获目标不能为空", nameof(targetName));
        if (_running) throw new InvalidOperationException("捕获运行时不能更换目标");
        TargetName = targetName;
        IsConfigured = true;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("未找到 VRChat 主窗口");
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
    public void PublishCapturedFrame(
        ReadOnlyMemory<byte> bgraPixels,
        int width,
        int height,
        DateTimeOffset? capturedAt = null,
        TimeSpan capturedTimestamp = default)
    {
        if (!_running) return;
        if (width <= 0 || height <= 0 || bgraPixels.Length < width * height * 4)
            throw new ArgumentException("捕获帧的尺寸或像素数据无效");
        var frame = new CapturedFrameEventArgs(
            Interlocked.Increment(ref _sequence),
            capturedAt ?? DateTimeOffset.UtcNow,
            bgraPixels,
            width,
            height,
            capturedTimestamp == default
                ? Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp())
                : capturedTimestamp);
        FrameArrived?.Invoke(this, frame);
    }

    /// <summary>Called by the Desktop capture adapter after a frame callback fails.</summary>
    public void PublishCaptureFailure(Exception error)
    {
        if (!_running) return;
        var args = new FrameSourceFailedEventArgs(error);
        foreach (EventHandler<FrameSourceFailedEventArgs> handler in
                 CaptureFailed?.GetInvocationList().Cast<EventHandler<FrameSourceFailedEventArgs>>() ?? [])
        {
            try { handler(this, args); }
            catch
            {
                // A subscriber must not turn a capture failure notification into
                // an unhandled exception on the native WGC callback thread.
            }
        }
    }
}
