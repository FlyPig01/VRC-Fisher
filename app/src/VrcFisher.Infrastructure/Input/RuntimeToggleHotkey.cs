using System.ComponentModel;
using System.Runtime.InteropServices;
using VrcFisher.Application;

namespace VrcFisher.Infrastructure.Input;

public sealed class RuntimeToggleHotkey(
    string gesture,
    Action callback,
    Func<bool> isTargetForeground,
    Action? targetLost = null) : IDisposable
{
    private const int AvailabilityHotkeyId = 0x5646;
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierShift = 0x0004;
    private const uint ModifierNoRepeat = 0x4000;
    private readonly ParsedGesture _gesture = Parse(gesture);
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _thread;

    public void Start()
    {
        if (_thread is not null) return;
        VerifyAvailability(_gesture);
        _thread = new Thread(Poll)
        {
            IsBackground = true,
            Name = $"VRC-Fisher-{_gesture.DisplayName}"
        };
        _thread.Start();
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        if (_thread is not null && _thread.IsAlive)
            _thread.Join(TimeSpan.FromSeconds(1));
        _cancellation.Dispose();
    }

    public static string CaptureGesture(uint virtualKey)
    {
        if (virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C)
            throw new ArgumentException("请继续按下功能键、字母或数字");

        var parts = new List<string>(4);
        if (IsKeyDown(0x11)) parts.Add("Ctrl");
        if (IsKeyDown(0x12)) parts.Add("Alt");
        if (IsKeyDown(0x10)) parts.Add("Shift");
        parts.Add(VirtualKeyName(virtualKey));
        var candidate = string.Join('+', parts);
        if (!HotkeyGestureRules.TryNormalize(candidate, out var normalized))
            throw new ArgumentException("功能键可单独使用；字母或数字必须搭配 Ctrl、Alt 或 Shift");
        return normalized;
    }

    private void Poll()
    {
        var wasForeground = false;
        var wasPressed = false;
        while (!_cancellation.IsCancellationRequested)
        {
            var foreground = false;
            try { foreground = isTargetForeground(); }
            catch { }

            if (wasForeground && !foreground)
            {
                try { targetLost?.Invoke(); }
                catch { }
            }
            wasForeground = foreground;

            if (!foreground)
            {
                wasPressed = false;
                _cancellation.Token.WaitHandle.WaitOne(20);
                continue;
            }

            var pressed = IsKeyDown(_gesture.VirtualKey)
                && (!_gesture.Control || IsKeyDown(0x11))
                && (!_gesture.Alt || IsKeyDown(0x12))
                && (!_gesture.Shift || IsKeyDown(0x10));
            if (pressed && !wasPressed)
            {
                try { callback(); }
                catch { }
            }
            wasPressed = pressed;
            _cancellation.Token.WaitHandle.WaitOne(15);
        }
    }

    private static void VerifyAvailability(ParsedGesture gesture)
    {
        var modifiers = ModifierNoRepeat
            | (gesture.Control ? ModifierControl : 0)
            | (gesture.Alt ? ModifierAlt : 0)
            | (gesture.Shift ? ModifierShift : 0);
        if (!RegisterHotKey(IntPtr.Zero, AvailabilityHotkeyId, modifiers, gesture.VirtualKey))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"热键 {gesture.DisplayName} 已被其他程序占用");
        UnregisterHotKey(IntPtr.Zero, AvailabilityHotkeyId);
    }

    private static ParsedGesture Parse(string value)
    {
        if (!HotkeyGestureRules.TryNormalize(value, out var normalized))
            throw new ArgumentOutOfRangeException(nameof(value), value, "不支持此热键");
        var tokens = normalized.Split('+');
        var key = tokens[^1];
        return new ParsedGesture(
            normalized,
            ParseVirtualKey(key),
            tokens.Contains("Ctrl", StringComparer.Ordinal),
            tokens.Contains("Alt", StringComparer.Ordinal),
            tokens.Contains("Shift", StringComparer.Ordinal));
    }

    private static uint ParseVirtualKey(string key)
    {
        if (key.Length >= 2 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var number))
            return (uint)(0x70 + number - 1);
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
            return key[0];
        throw new ArgumentOutOfRangeException(nameof(key));
    }

    private static string VirtualKeyName(uint virtualKey)
    {
        if (virtualKey is >= 0x70 and <= 0x87) return $"F{virtualKey - 0x70 + 1}";
        if (virtualKey is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) return ((char)virtualKey).ToString();
        throw new ArgumentException("仅支持 F1-F24、字母和数字键");
    }

    private static bool IsKeyDown(uint virtualKey) => (GetAsyncKeyState((int)virtualKey) & 0x8000) != 0;

    private readonly record struct ParsedGesture(
        string DisplayName,
        uint VirtualKey,
        bool Control,
        bool Alt,
        bool Shift);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
