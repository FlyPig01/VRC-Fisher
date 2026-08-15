using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace VrcFisher.Desktop.Capture;

internal static class GraphicsCaptureItemFactory
{
    private static readonly Guid GraphicsCaptureItemId =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForWindow(IntPtr window)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        var itemPointer = interop.CreateForWindow(window, in GraphicsCaptureItemId);
        if (itemPointer == IntPtr.Zero)
            throw new InvalidOperationException("Windows Graphics Capture did not return a VRChat capture item.");

        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, in Guid interfaceId);
        IntPtr CreateForMonitor(IntPtr monitor, in Guid interfaceId);
    }
}
