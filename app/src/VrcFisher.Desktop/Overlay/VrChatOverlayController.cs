using Microsoft.UI.Xaml;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Desktop.Capture;
using VrcFisher.Desktop.Localization;

namespace VrcFisher.Desktop.Overlay;

internal sealed class VrChatOverlayController : IDisposable
{
    public const int RefreshRateHz = 15;

    private readonly IRuntimeController _runtime;
    private readonly IDetectionRuntime _detection;
    private readonly WgcCaptureAdapter _capture;
    private readonly Func<AppOptions> _optionsProvider;
    private readonly Action<Exception> _failureHandler;
    private readonly DetectionDisplaySmoother _smoother = new();
    private readonly NativeVrChatOverlay _overlay = new();
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1000d / RefreshRateHz)
    };
    private DetectionVisualizationFrame? _pendingFrame;
    private bool _wasRunning;
    private bool _wasDebug;
    private bool _failed;
    private bool _disposed;

    public VrChatOverlayController(
        IRuntimeController runtime,
        IDetectionRuntime detection,
        WgcCaptureAdapter capture,
        Func<AppOptions> optionsProvider,
        Action<Exception> failureHandler)
    {
        _runtime = runtime;
        _detection = detection;
        _capture = capture;
        _optionsProvider = optionsProvider;
        _failureHandler = failureHandler;
        _detection.VisualizationChanged += OnVisualizationChanged;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _detection.VisualizationChanged -= OnVisualizationChanged;
        _overlay.HideAll();
        _overlay.Dispose();
    }

    private void OnVisualizationChanged(object? sender, DetectionVisualizationFrame frame) =>
        Interlocked.Exchange(ref _pendingFrame, frame);

    private void OnTick(object? sender, object args)
    {
        if (_failed || _disposed) return;
        try
        {
            Render();
        }
        catch (Exception error)
        {
            _failed = true;
            _timer.Stop();
            _overlay.HideAll();
            _failureHandler(error);
        }
    }

    private void Render()
    {
        var running = _runtime.Snapshot.IsObserving;
        var options = _optionsProvider();
        var debug = options.WorkMode == ApplicationMode.Debug;
        if (!running
            || !NativeVrChatOverlay.TryGetVisibleClientBounds(
                _capture.TargetWindow,
                out var bounds,
                out var scale))
        {
            if (_wasRunning) ResetVisualization();
            _overlay.HideAll();
            _wasRunning = running;
            _wasDebug = debug;
            return;
        }

        _wasRunning = true;
        _overlay.ShowPrompt(
            bounds,
            scale,
            options.WorkMode,
            UiStrings.Format("OverlayStopHint", options.ToggleHotkey));
        if (!debug)
        {
            if (_wasDebug) ResetVisualization();
            _overlay.HideDetections();
            _wasDebug = false;
            return;
        }

        _wasDebug = true;
        var pending = Interlocked.Exchange(ref _pendingFrame, null);
        if (pending is not null) _smoother.Push(pending);
        var current = _smoother.GetCurrent(DateTimeOffset.UtcNow);
        if (current is null) _overlay.HideDetections();
        else _overlay.ShowDetections(bounds, scale, current);
    }

    private void ResetVisualization()
    {
        Interlocked.Exchange(ref _pendingFrame, null);
        _smoother.Reset();
    }
}
