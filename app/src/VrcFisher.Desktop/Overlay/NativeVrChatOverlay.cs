using System.Globalization;
using System.Runtime.InteropServices;
using VrcFisher.Core;

namespace VrcFisher.Desktop.Overlay;

internal sealed class NativeVrChatOverlay : IDisposable
{
    private static readonly uint PromptForeground = NativeOverlaySurface.Color(255, 255, 255);
    private static readonly uint HintForeground = NativeOverlaySurface.Color(194, 194, 200);
    private static readonly uint PromptBackground = NativeOverlaySurface.Color(28, 28, 30);
    private static readonly uint RunAccentBackground = NativeOverlaySurface.Color(0, 112, 210);
    private static readonly uint DebugAccentBackground = NativeOverlaySurface.Color(196, 117, 0);
    private static readonly uint ErrorBackground = NativeOverlaySurface.Color(176, 36, 48);
    private static readonly IReadOnlyDictionary<string, uint> ClassColors =
        new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            ["bite_indicator"] = NativeOverlaySurface.Color(255, 202, 58),
            ["minigame_panel"] = NativeOverlaySurface.Color(255, 77, 109),
            ["catch_zone"] = NativeOverlaySurface.Color(51, 209, 122),
            ["moving_target"] = NativeOverlaySurface.Color(78, 161, 255)
        };

    private readonly Dictionary<string, DetectionBoxOverlay> _boxes = ClassColors
        .ToDictionary(item => item.Key, item => new DetectionBoxOverlay(item.Value), StringComparer.Ordinal);
    private readonly NativeOverlaySurface _prompt = new(235);
    private readonly NativeOverlaySurface _promptHint = new(235);
    private readonly NativeOverlaySurface _modeIcon = new(235);
    private PromptLayout? _promptLayout;
    private bool _promptVisible;
    private bool _disposed;

    public static OverlayBoundsStatus GetVisibleClientBounds(IntPtr target, out ScreenBounds bounds, out double scale)
    {
        bounds = default;
        scale = 1;
        if (target == IntPtr.Zero
            || !IsWindow(target)
            || !IsWindowVisible(target)
            || IsIconic(target)
            || GetForegroundWindow() != target)
        {
            return OverlayBoundsStatus.TargetUnavailable;
        }
        if (!GetClientRect(target, out var client))
            return OverlayBoundsStatus.TransientFailure;

        var topLeft = new Point { X = client.Left, Y = client.Top };
        var bottomRight = new Point { X = client.Right, Y = client.Bottom };
        if (!ClientToScreen(target, ref topLeft) || !ClientToScreen(target, ref bottomRight))
            return OverlayBoundsStatus.TransientFailure;

        var width = bottomRight.X - topLeft.X;
        var height = bottomRight.Y - topLeft.Y;
        if (width <= 0 || height <= 0) return OverlayBoundsStatus.TransientFailure;

        bounds = new ScreenBounds(topLeft.X, topLeft.Y, width, height);
        var dpi = GetDpiForWindow(target);
        scale = dpi == 0 ? 1 : dpi / 96d;
        return OverlayBoundsStatus.Available;
    }

    public void ShowPrompt(
        ScreenBounds bounds,
        double scale,
        ApplicationMode mode,
        string primaryText,
        string secondaryText,
        bool isError = false)
    {
        var layout = new PromptLayout(bounds, scale, mode, primaryText, secondaryText, isError);
        if (_promptVisible && _promptLayout == layout) return;

        var primaryFontHeight = Math.Max(16, (int)Math.Round(17 * scale));
        var secondaryFontHeight = Math.Max(12, (int)Math.Round(12 * scale));
        var paddingX = Math.Max(14, (int)Math.Round(14 * scale));
        var primaryPaddingY = Math.Max(6, (int)Math.Round(6 * scale));
        var secondaryPaddingY = Math.Max(5, (int)Math.Round(5 * scale));
        var margin = Math.Max(16, (int)Math.Round(18 * scale));
        var primaryMeasured = _prompt.MeasureText(primaryText, primaryFontHeight);
        var secondaryMeasured = string.IsNullOrWhiteSpace(secondaryText)
            ? default
            : _promptHint.MeasureText(secondaryText, secondaryFontHeight);
        var primaryHeight = primaryMeasured.Height + primaryPaddingY * 2;
        var secondaryHeight = string.IsNullOrWhiteSpace(secondaryText)
            ? 0
            : secondaryMeasured.Height + secondaryPaddingY * 2;
        var height = primaryHeight + secondaryHeight;
        var iconWidth = Math.Max(height, (int)Math.Round(44 * scale));
        var availableWidth = Math.Max(1, bounds.Width - margin * 2 - iconWidth);
        var contentWidth = Math.Max(primaryMeasured.Width, secondaryMeasured.Width) + paddingX * 2;
        var width = Math.Max(1, Math.Min(availableWidth, contentWidth));
        var promptX = bounds.X + bounds.Width - margin - width;
        var promptY = bounds.Y + margin;
        var background = isError ? ErrorBackground : PromptBackground;
        var iconBackground = isError
            ? ErrorBackground
            : mode == ApplicationMode.Debug
                ? DebugAccentBackground
                : RunAccentBackground;
        _prompt.ShowText(
            primaryText,
            promptX,
            promptY,
            width,
            primaryHeight,
            primaryFontHeight,
            PromptForeground,
            background);
        if (secondaryHeight == 0)
        {
            _promptHint.Hide();
        }
        else
        {
            _promptHint.ShowText(
                secondaryText,
                promptX,
                promptY + primaryHeight,
                width,
                secondaryHeight,
                secondaryFontHeight,
                HintForeground,
                background);
        }
        _modeIcon.ShowText(
            mode == ApplicationMode.Debug ? "\uEBE8" : "\u25B6",
            promptX - iconWidth,
            promptY,
            iconWidth,
            height,
            primaryFontHeight,
            PromptForeground,
            iconBackground,
            mode == ApplicationMode.Debug ? "Segoe Fluent Icons" : "Segoe UI Symbol");
        _promptLayout = layout;
        _promptVisible = true;
    }

    public void ShowDetections(ScreenBounds bounds, double scale, DetectionVisualizationFrame frame)
    {
        var visible = new HashSet<string>(StringComparer.Ordinal);
        var scaleX = (double)bounds.Width / frame.Width;
        var scaleY = (double)bounds.Height / frame.Height;
        foreach (var detection in frame.Detections)
        {
            if (!_boxes.TryGetValue(detection.ClassName, out var overlay)) continue;
            var left = bounds.X + (int)Math.Round(detection.Box.Left * scaleX);
            var top = bounds.Y + (int)Math.Round(detection.Box.Top * scaleY);
            var right = bounds.X + (int)Math.Round(detection.Box.Right * scaleX);
            var bottom = bounds.Y + (int)Math.Round(detection.Box.Bottom * scaleY);
            left = Math.Clamp(left, bounds.X, bounds.X + bounds.Width);
            right = Math.Clamp(right, bounds.X, bounds.X + bounds.Width);
            top = Math.Clamp(top, bounds.Y, bounds.Y + bounds.Height);
            bottom = Math.Clamp(bottom, bounds.Y, bounds.Y + bounds.Height);
            if (right - left < 4 || bottom - top < 4) continue;

            overlay.Show(
                new ScreenBounds(left, top, right - left, bottom - top),
                scale,
                Math.Clamp(detection.Confidence, 0, 1).ToString("0.00", CultureInfo.InvariantCulture),
                bounds);
            visible.Add(detection.ClassName);
        }

        foreach (var item in _boxes)
            if (!visible.Contains(item.Key)) item.Value.Hide();
    }

    public void HideDetections()
    {
        foreach (var box in _boxes.Values) box.Hide();
    }

    public void HideAll()
    {
        _prompt.Hide();
        _promptHint.Hide();
        _modeIcon.Hide();
        _promptVisible = false;
        HideDetections();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _prompt.Dispose();
        _promptHint.Dispose();
        _modeIcon.Dispose();
        foreach (var box in _boxes.Values) box.Dispose();
    }

    private sealed class DetectionBoxOverlay(uint color) : IDisposable
    {
        private readonly NativeOverlaySurface _top = new();
        private readonly NativeOverlaySurface _right = new();
        private readonly NativeOverlaySurface _bottom = new();
        private readonly NativeOverlaySurface _left = new();
        private readonly NativeOverlaySurface _confidence = new(235);

        public void Show(ScreenBounds box, double scale, string confidence, ScreenBounds target)
        {
            var thickness = Math.Max(2, (int)Math.Round(2 * scale));
            _top.ShowRectangle(box.X, box.Y, box.Width, thickness, color);
            _bottom.ShowRectangle(box.X, box.Y + box.Height - thickness, box.Width, thickness, color);
            _left.ShowRectangle(box.X, box.Y, thickness, box.Height, color);
            _right.ShowRectangle(box.X + box.Width - thickness, box.Y, thickness, box.Height, color);

            var fontHeight = Math.Max(14, (int)Math.Round(14 * scale));
            var paddingX = Math.Max(7, (int)Math.Round(7 * scale));
            var paddingY = Math.Max(4, (int)Math.Round(4 * scale));
            var measured = _confidence.MeasureText(confidence, fontHeight);
            var labelWidth = measured.Width + paddingX * 2;
            var labelHeight = measured.Height + paddingY * 2;
            var labelX = Math.Clamp(box.X, target.X, target.X + target.Width - labelWidth);
            var labelY = box.Y - labelHeight;
            if (labelY < target.Y) labelY = box.Y + thickness;
            _confidence.ShowText(
                confidence,
                labelX,
                labelY,
                labelWidth,
                labelHeight,
                fontHeight,
                color,
                PromptBackground);
        }

        public void Hide()
        {
            _top.Hide();
            _right.Hide();
            _bottom.Hide();
            _left.Hide();
            _confidence.Hide();
        }

        public void Dispose()
        {
            _top.Dispose();
            _right.Dispose();
            _bottom.Dispose();
            _left.Dispose();
            _confidence.Dispose();
        }
    }

    internal readonly record struct ScreenBounds(int X, int Y, int Width, int Height);

    internal enum OverlayBoundsStatus
    {
        Available,
        TransientFailure,
        TargetUnavailable
    }

    private readonly record struct PromptLayout(
        ScreenBounds Bounds,
        double Scale,
        ApplicationMode Mode,
        string PrimaryText,
        string SecondaryText,
        bool IsError);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rectangle rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref Point point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
