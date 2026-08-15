using System.Diagnostics;
using System.Runtime.InteropServices;
using VrcFisher.Core;

namespace VrcFisher.Desktop.Capture;

internal static class VrChatWindowLocator
{
    public static IntPtr FindMainWindow()
    {
        foreach (var process in Process.GetProcessesByName(TargetApplication.ProcessName))
        {
            using (process)
            {
                try
                {
                    process.Refresh();
                    var window = process.MainWindowHandle;
                    if (window != IntPtr.Zero && IsWindow(window)) return window;
                }
                catch (InvalidOperationException)
                {
                    // The process exited while it was being inspected.
                }
            }
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);
}
