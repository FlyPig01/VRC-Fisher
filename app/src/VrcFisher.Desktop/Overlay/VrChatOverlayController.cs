using Microsoft.UI.Xaml;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Desktop.Capture;
using VrcFisher.Desktop.Localization;

namespace VrcFisher.Desktop.Overlay;

internal sealed class VrChatOverlayController : IDisposable
{
    public const int RefreshRateHz = 30;
    private const int TransientBoundsFailureLimit = 3;

    private readonly IRuntimeController _runtime;
    private readonly IDetectionRuntime _detection;
    private readonly WgcCaptureAdapter _capture;
    private readonly Func<AppOptions> _optionsProvider;
    private readonly Action<Exception> _failureHandler;
    private readonly DetectionDisplayBuffer _displayBuffer = new();
    private readonly NativeVrChatOverlay _overlay = new();
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1000d / RefreshRateHz)
    };
    private DetectionVisualizationFrame? _pendingFrame;
    private DateTimeOffset _lastSnapshotUpdate;
    private DateTimeOffset _failureVisibleUntil;
    private bool _wasVisible;
    private bool _wasDebug;
    private int _transientBoundsFailures;
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
        var now = DateTimeOffset.UtcNow;
        var snapshot = _runtime.Snapshot;
        if (snapshot.UpdatedAt != _lastSnapshotUpdate)
        {
            _lastSnapshotUpdate = snapshot.UpdatedAt;
            if (snapshot.Lifecycle == RuntimeLifecycle.Stopped
                && (snapshot.Status.Code is RuntimeMessageCode.DetectionStopped
                    or RuntimeMessageCode.UnexpectedFailure
                    or RuntimeMessageCode.InputFailed))
            {
                _failureVisibleUntil = now.AddSeconds(6);
            }
        }

        var starting = snapshot.Lifecycle == RuntimeLifecycle.Starting;
        var running = snapshot.Lifecycle == RuntimeLifecycle.Running && snapshot.IsObserving;
        var failed = snapshot.Lifecycle == RuntimeLifecycle.Stopped && now < _failureVisibleUntil;
        var visible = starting || running || failed;
        var options = _optionsProvider();
        var debug = options.WorkMode == ApplicationMode.Debug;
        if (!visible)
        {
            HideAll();
            return;
        }

        var boundsStatus = NativeVrChatOverlay.GetVisibleClientBounds(
            _capture.TargetWindow,
            out var bounds,
            out var scale);
        if (boundsStatus == NativeVrChatOverlay.OverlayBoundsStatus.TargetUnavailable)
        {
            HideAll();
            return;
        }
        if (boundsStatus == NativeVrChatOverlay.OverlayBoundsStatus.TransientFailure)
        {
            _transientBoundsFailures++;
            if (_transientBoundsFailures >= TransientBoundsFailureLimit)
                HideAll();
            return;
        }

        _transientBoundsFailures = 0;
        _wasVisible = true;
        var primaryText = starting
            ? UiStrings.Get("OverlayStarting")
            : failed
                ? UiStrings.Get("OverlayStartFailed")
                : UiStrings.OverlayStage(snapshot.Phase);
        var secondaryText = failed
            ? string.Empty
            : UiStrings.Format("OverlayStopKeyHint", options.ToggleHotkey);
        _overlay.ShowPrompt(
            bounds,
            scale,
            options.WorkMode,
            primaryText,
            secondaryText,
            failed);
        if (!running || !debug)
        {
            if (_wasDebug) ResetVisualization();
            _overlay.HideDetections();
            _wasDebug = false;
            return;
        }

        _wasDebug = true;
        var pending = Interlocked.Exchange(ref _pendingFrame, null);
        if (pending is not null) _displayBuffer.Push(pending);
        var current = _displayBuffer.GetCurrent(DateTimeOffset.UtcNow);
        if (current is null) _overlay.HideDetections();
        else _overlay.ShowDetections(bounds, scale, current);
    }

    private void ResetVisualization()
    {
        Interlocked.Exchange(ref _pendingFrame, null);
        _displayBuffer.Reset();
    }

    private void HideAll()
    {
        if (_wasVisible) ResetVisualization();
        _overlay.HideAll();
        _wasVisible = false;
        _wasDebug = false;
        _transientBoundsFailures = 0;
    }
}
