using VrcFisher.Core;
using VrcFisher.Desktop.Contracts;
using VrcFisher.Infrastructure.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Foundation;
using System.Runtime.InteropServices;
using WinRT;

namespace VrcFisher.Desktop.Capture;

/// <summary>
/// Desktop-only Windows Graphics Capture adapter. It owns WinRT objects and
/// publishes CPU-readable BGRA frames to the Infrastructure source boundary.
/// </summary>
internal sealed class WgcCaptureAdapter(WindowsGraphicsCaptureSource source) : IFrameSource, IAsyncDisposable, ICaptureTargetState
{
    private readonly SemaphoreSlim _frameGate = new(1, 1);
    private readonly object _sync = new();
    private GraphicsCaptureItem? _item;
    private IDirect3DDevice? _device;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private IntPtr _targetWindow;
    private bool _running;

    public event EventHandler<CapturedFrameEventArgs>? FrameArrived
    {
        add => source.FrameArrived += value;
        remove => source.FrameArrived -= value;
    }

    public event EventHandler? TargetChanged;

    public bool IsConfigured
    {
        get { lock (_sync) return _item is not null; }
    }

    public string TargetName => TargetApplication.ProcessName;
    public bool IsSupported => GraphicsCaptureSession.IsSupported();
    internal IntPtr TargetWindow
    {
        get { lock (_sync) return _targetWindow; }
    }

    public bool RefreshVrChatTarget()
    {
        lock (_sync)
        {
            if (_running) return _item is not null;
        }

        var window = IsSupported ? VrChatWindowLocator.FindMainWindow() : IntPtr.Zero;
        if (window == IntPtr.Zero)
        {
            ClearTarget();
            return false;
        }

        lock (_sync)
        {
            if (_item is not null && _targetWindow == window) return true;
        }

        GraphicsCaptureItem item;
        try
        {
            item = GraphicsCaptureItemFactory.CreateForWindow(window);
        }
        catch (Exception error) when (error is COMException or InvalidCastException)
        {
            ClearTarget();
            return false;
        }

        GraphicsCaptureItem? previous;
        lock (_sync)
        {
            if (_running)
            {
                return _item is not null;
            }
            previous = _item;
            if (previous is not null) previous.Closed -= OnTargetClosed;
            _item = item;
            _targetWindow = window;
            _item.Closed += OnTargetClosed;
            source.Configure(TargetApplication.ProcessName);
        }
        TargetChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!RefreshVrChatTarget())
            throw new InvalidOperationException("VRChat is not running or its main window is unavailable.");
        lock (_sync)
        {
            if (_running) return Task.CompletedTask;
            if (_item is null) throw new InvalidOperationException("VRChat is not running or its main window is unavailable.");
            if (!IsSupported) throw new PlatformNotSupportedException("当前系统不支持 Windows Graphics Capture");
            _device = Direct3DDeviceFactory.Create();
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _item.Size);
            _pool.FrameArrived += OnFrameArrived;
            _session = _pool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = false;
            _running = true;
            source.StartAsync(cancellationToken).GetAwaiter().GetResult();
            _session.StartCapture();
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Direct3D11CaptureFramePool? pool;
        GraphicsCaptureSession? session;
        IDirect3DDevice? device;
        lock (_sync)
        {
            _running = false;
            pool = _pool;
            session = _session;
            device = _device;
            _pool = null;
            _session = null;
            _device = null;
        }
        if (pool is not null) pool.FrameArrived -= OnFrameArrived;
        session?.Dispose();
        pool?.Dispose();
        await _frameGate.WaitAsync(cancellationToken);
        _frameGate.Release();
        device?.Dispose();
        await source.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        ClearTarget();
        _frameGate.Dispose();
        await source.DisposeAsync();
    }

    private void ClearTarget()
    {
        GraphicsCaptureItem? previous;
        lock (_sync)
        {
            if (_item is null && _targetWindow == IntPtr.Zero) return;
            previous = _item;
            if (previous is not null) previous.Closed -= OnTargetClosed;
            _item = null;
            _targetWindow = IntPtr.Zero;
        }
        TargetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTargetClosed(GraphicsCaptureItem sender, object args)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_item, sender)) return;
            sender.Closed -= OnTargetClosed;
            _item = null;
            _targetWindow = IntPtr.Zero;
        }
        TargetChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (!await _frameGate.WaitAsync(0)) return;
        try
        {
            while (_running)
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null) break;
                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
                using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
                var plane = buffer.GetPlaneDescription(0);
                using var reference = buffer.CreateReference();
                source.PublishCapturedFrame(CopyBgra(reference, plane, bitmap.PixelWidth, bitmap.PixelHeight), bitmap.PixelWidth, bitmap.PixelHeight);
            }
        }
        catch (Exception) when (!_running)
        {
            // Closing a frame pool may race with its final event.
        }
        finally
        {
            _frameGate.Release();
        }
    }

    private static unsafe byte[] CopyBgra(IMemoryBufferReference reference, BitmapPlaneDescription plane, int width, int height)
    {
        var access = (IMemoryBufferByteAccess)reference;
        access.GetBuffer(out var source, out _);
        var rowBytes = checked(width * 4);
        var output = new byte[checked(rowBytes * height)];
        for (var row = 0; row < height; row++)
            Marshal.Copy((IntPtr)(source + plane.StartIndex + row * plane.Stride), output, row * rowBytes, rowBytes);
        return output;
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0D8D3D57")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}

internal static class Direct3DDeviceFactory
{
    private static readonly Guid IidDxgiDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private const uint D3D11CreateDeviceFlagBgraSupport = 0x20;
    private const uint D3DFeatureLevel11_0 = 0xb000;
    private const uint D3DDriverTypeHardware = 1;

    public static IDirect3DDevice Create()
    {
        var result = D3D11CreateDevice(
            IntPtr.Zero,
            D3DDriverTypeHardware,
            IntPtr.Zero,
            D3D11CreateDeviceFlagBgraSupport,
            [D3DFeatureLevel11_0],
            1,
            7,
            out var device,
            out _,
            out _);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in IidDxgiDevice, out var dxgiDevice));
            IntPtr abi;
            try
            {
                result = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out abi);
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
            Marshal.ThrowExceptionForHR(result);
            try { return MarshalInterface<IDirect3DDevice>.FromAbi(abi); }
            finally { Marshal.Release(abi); }
        }
        finally { Marshal.Release(device); }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        uint driverType,
        IntPtr software,
        uint flags,
        uint[] featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out IntPtr device,
        out uint featureLevel,
        out IntPtr context);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
