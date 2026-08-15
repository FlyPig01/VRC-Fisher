using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VrcFisher.Infrastructure.Input;

public sealed class RuntimeToggleHotkey(string key, Action callback) : IDisposable
{
    private const int HotkeyId = 0x5646;
    private readonly uint _virtualKey = ParseVirtualKey(key);
    private Thread? _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new(false);
    private bool _registered;
    private int _registrationError;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = $"VRC-Fisher-{key}" };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(2)))
            throw new TimeoutException($"注册热键 {key} 超时");
        if (!_registered)
            throw new Win32Exception(_registrationError, $"热键 {key} 已被其他程序占用");
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, 0x0012, UIntPtr.Zero, IntPtr.Zero);
        if (_thread is not null && _thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(1));
        _ready.Dispose();
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _registered = RegisterHotKey(IntPtr.Zero, HotkeyId, 0, _virtualKey);
        if (!_registered) _registrationError = Marshal.GetLastWin32Error();
        _ready.Set();
        if (!_registered) return;
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.Message == 0x0312 && message.WParam == (UIntPtr)HotkeyId) callback();
        }
        UnregisterHotKey(IntPtr.Zero, HotkeyId);
    }

    private static uint ParseVirtualKey(string value)
    {
        if (value.Length >= 2
            && value[0] == 'F'
            && int.TryParse(value.AsSpan(1), out var number)
            && number is >= 1 and <= 24)
            return (uint)(0x70 + number - 1);
        throw new ArgumentOutOfRangeException(nameof(value), value, "只支持 F1-F24");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam, uint time, int x, int y)
    {
        public IntPtr HWnd = hWnd;
        public uint Message = message;
        public UIntPtr WParam = wParam;
        public IntPtr LParam = lParam;
        public uint Time = time;
        public int X = x;
        public int Y = y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
