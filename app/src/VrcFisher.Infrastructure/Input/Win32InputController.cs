using System.Runtime.InteropServices;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Input;

public sealed class Win32InputController : IInputController
{
    private readonly object _sync = new();
    private bool _leftDown;

    public void Click()
    {
        lock (_sync)
        {
            mouse_event(MouseEventFlags.LeftDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseEventFlags.LeftUp, 0, 0, 0, UIntPtr.Zero);
        }
    }

    public void PressLeft()
    {
        lock (_sync)
        {
            if (_leftDown) return;
            mouse_event(MouseEventFlags.LeftDown, 0, 0, 0, UIntPtr.Zero);
            _leftDown = true;
        }
    }

    public void ReleaseLeft()
    {
        lock (_sync)
        {
            if (!_leftDown) return;
            mouse_event(MouseEventFlags.LeftUp, 0, 0, 0, UIntPtr.Zero);
            _leftDown = false;
        }
    }

    public void ReleaseAll() => ReleaseLeft();

    [Flags]
    private enum MouseEventFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(MouseEventFlags flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
