using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VrcFisher.Desktop.Overlay;

internal sealed class NativeOverlaySurface : IDisposable
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int SwHide = 0;
    private const uint LwaAlpha = 0x00000002;
    private const int TransparentBackground = 1;
    private const uint DtCenter = 0x00000001;
    private const uint DtVCenter = 0x00000004;
    private const uint DtSingleLine = 0x00000020;
    private const uint DtNoPrefix = 0x00000800;
    private const uint WmPaint = 0x000F;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmNcHitTest = 0x0084;
    private const int ErrorClassAlreadyExists = 1410;
    private static readonly IntPtr Topmost = new(-1);
    private static readonly string WindowClassName = $"VrcFisherOverlay_{Environment.ProcessId}";
    private static readonly WindowProcedure Procedure = WindowProc;
    private static readonly Dictionary<IntPtr, NativeOverlaySurface> Instances = [];
    private static readonly object InstancesLock = new();
    private static bool _registered;

    private readonly IntPtr _window;
    private IntPtr _font;
    private uint _background;
    private uint _foreground;
    private string _text = string.Empty;
    private int _fontHeight;
    private bool _disposed;

    public NativeOverlaySurface(byte opacity = byte.MaxValue)
    {
        EnsureWindowClass();
        _window = CreateWindowExW(
            WsExTopmost | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate,
            WindowClassName,
            string.Empty,
            WsPopup,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero);
        if (_window == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create the VRChat overlay window.");

        lock (InstancesLock) Instances[_window] = this;
        if (!SetLayeredWindowAttributes(_window, 0, opacity, LwaAlpha))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to configure the VRChat overlay window.");
    }

    public void ShowRectangle(int x, int y, int width, int height, uint color)
    {
        _text = string.Empty;
        _background = color;
        Show(x, y, width, height);
    }

    public Size MeasureText(string text, int fontHeight)
    {
        EnsureFont(fontHeight);
        var dc = GetDC(_window);
        if (dc == IntPtr.Zero)
            return new Size { Width = Math.Max(1, text.Length * fontHeight), Height = fontHeight };
        var previous = SelectObject(dc, _font);
        try
        {
            return GetTextExtentPoint32W(dc, text, text.Length, out var size)
                ? size
                : new Size { Width = Math.Max(1, text.Length * fontHeight), Height = fontHeight };
        }
        finally
        {
            SelectObject(dc, previous);
            ReleaseDC(_window, dc);
        }
    }

    public void ShowText(
        string text,
        int x,
        int y,
        int width,
        int height,
        int fontHeight,
        uint foreground,
        uint background)
    {
        _text = text;
        _foreground = foreground;
        _background = background;
        EnsureFont(fontHeight);
        Show(x, y, width, height);
    }

    public void Hide()
    {
        if (!_disposed) ShowWindow(_window, SwHide);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (InstancesLock) Instances.Remove(_window);
        if (_font != IntPtr.Zero)
        {
            DeleteObject(_font);
            _font = IntPtr.Zero;
        }
        DestroyWindow(_window);
    }

    private void Show(int x, int y, int width, int height)
    {
        if (_disposed || width <= 0 || height <= 0) return;
        SetWindowPos(
            _window,
            Topmost,
            x,
            y,
            width,
            height,
            SwpNoActivate | SwpShowWindow);
        InvalidateRect(_window, IntPtr.Zero, false);
    }

    private void EnsureFont(int height)
    {
        height = Math.Max(12, height);
        if (_font != IntPtr.Zero && _fontHeight == height) return;
        if (_font != IntPtr.Zero) DeleteObject(_font);
        _font = CreateFontW(
            -height,
            0,
            0,
            0,
            600,
            0,
            0,
            0,
            1,
            0,
            0,
            5,
            0,
            "Segoe UI");
        if (_font == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create the overlay font.");
        _fontHeight = height;
    }

    private void Paint()
    {
        var dc = GetDC(_window);
        if (dc == IntPtr.Zero) return;
        try
        {
            GetClientRect(_window, out var rectangle);
            var brush = CreateSolidBrush(_background);
            if (brush != IntPtr.Zero)
            {
                FillRect(dc, in rectangle, brush);
                DeleteObject(brush);
            }
            if (_text.Length == 0 || _font == IntPtr.Zero) return;
            var previous = SelectObject(dc, _font);
            SetBkMode(dc, TransparentBackground);
            SetTextColor(dc, _foreground);
            DrawTextW(dc, _text, _text.Length, ref rectangle, DtCenter | DtVCenter | DtSingleLine | DtNoPrefix);
            SelectObject(dc, previous);
        }
        finally
        {
            ReleaseDC(_window, dc);
            ValidateRect(_window, IntPtr.Zero);
        }
    }

    private static void EnsureWindowClass()
    {
        if (_registered) return;
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = Procedure,
            Instance = GetModuleHandleW(null),
            ClassName = WindowClassName
        };
        if (RegisterClassExW(in windowClass) == 0
            && Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register the VRChat overlay window class.");
        }
        _registered = true;
    }

    private static IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmEraseBackground) return new IntPtr(1);
        if (message == WmNcHitTest) return new IntPtr(-1);
        if (message == WmPaint)
        {
            NativeOverlaySurface? surface;
            lock (InstancesLock) Instances.TryGetValue(window, out surface);
            surface?.Paint();
            return IntPtr.Zero;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    public static uint Color(byte red, byte green, byte blue) =>
        (uint)(red | green << 8 | blue << 16);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public WindowProcedure? Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string? ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size
    {
        public int Width;
        public int Height;
    }

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(in WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rectangle rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(IntPtr window, IntPtr rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ValidateRect(IntPtr window, IntPtr rectangle);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr deviceContext, in Rectangle rectangle, IntPtr brush);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint characterSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr deviceContext, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr deviceContext, uint color);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextExtentPoint32W(
        IntPtr deviceContext,
        string text,
        int count,
        out Size size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(
        IntPtr deviceContext,
        string text,
        int count,
        ref Rectangle rectangle,
        uint format);
}
