using System.Diagnostics;
using System.Runtime.InteropServices;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Input;

public sealed class VrChatForegroundState
{
    public bool IsForeground
    {
        get
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;
            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == 0) return false;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                return string.Equals(
                    process.ProcessName,
                    TargetApplication.ProcessName,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
