using System.Runtime.InteropServices;

namespace VrcFisher.Infrastructure.Input;

public sealed class EmergencyStopHotkey(Action callback) : IDisposable
{
    private const int HotkeyId = 0x5646;
    private const uint VkF8 = 0x77;
    private Thread? _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new(false);

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "VRC-Fisher-F8" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
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
        RegisterHotKey(IntPtr.Zero, HotkeyId, 0, VkF8);
        _ready.Set();
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.Message == 0x0312 && message.WParam == (UIntPtr)HotkeyId) callback();
        }
        UnregisterHotKey(IntPtr.Zero, HotkeyId);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeMessage(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam, uint time, int x, int y)
    {
        public IntPtr HWnd = hWnd;
        public uint Message = message;
        public UIntPtr WParam = wParam;
        public IntPtr LParam = lParam;
        public uint Time = time;
        public int X = x;
        public int Y = y;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out NativeMessage message, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
