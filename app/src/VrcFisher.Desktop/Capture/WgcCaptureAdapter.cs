using VrcFisher.Core;
using VrcFisher.Desktop.Contracts;
using VrcFisher.Infrastructure.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Foundation;
using Windows.Storage.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinRT;

namespace VrcFisher.Desktop.Capture;

/// <summary>
/// Desktop-only Windows Graphics Capture adapter. It owns WinRT objects and
/// publishes CPU-readable BGRA frames to the Infrastructure source boundary.
/// </summary>
internal sealed class WgcCaptureAdapter(
    WindowsGraphicsCaptureSource source,
    ILogger<WgcCaptureAdapter> logger) : IDemandDrivenFrameSource, IAsyncDisposable, ICaptureTargetState
{
    private readonly DispatcherQueueController _captureThread =
        DispatcherQueueController.CreateOnDedicatedThread();
    private readonly SemaphoreSlim _frameGate = new(1, 1);
    private readonly FrameReadbackGate _readbackGate = new();
    private readonly CaptureReadbackStatistics _statistics = new();
    private readonly object _sync = new();
    private GraphicsCaptureItem? _item;
    private TypedEventHandler<GraphicsCaptureItem, object>? _itemClosedHandler;
    private IDirect3DDevice? _device;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private IntPtr _targetWindow;
    private Windows.Storage.Streams.Buffer? _pixelBuffer;
    private byte[]? _pixelBytes;
    private volatile bool _running;
    private int _disposed;

    public event EventHandler<CapturedFrameEventArgs>? FrameArrived
    {
        add => source.FrameArrived += value;
        remove => source.FrameArrived -= value;
    }

    public event EventHandler<FrameSourceFailedEventArgs>? CaptureFailed
    {
        add => source.CaptureFailed += value;
        remove => source.CaptureFailed -= value;
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

    public void RequestNextFrame(TimeSpan delay) => _readbackGate.Request(delay);

    public bool RefreshVrChatTarget() =>
        InvokeOnCaptureThread(RefreshVrChatTargetCore);

    private bool RefreshVrChatTargetCore()
    {
        lock (_sync)
        {
            if (_running) return _item is not null;
        }

        var window = IsSupported ? VrChatWindowLocator.FindMainWindow() : IntPtr.Zero;
        if (window == IntPtr.Zero)
        {
            ClearTargetCore();
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
            LogWinRtFailure("refresh_target", error);
            ClearTargetCore();
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
            if (previous is not null && _itemClosedHandler is not null)
                previous.Closed -= _itemClosedHandler;
            _item = item;
            _targetWindow = window;
            _itemClosedHandler = (_, _) =>
                PostToCaptureThread(() => HandleTargetClosed(item));
            _item.Closed += _itemClosedHandler;
            source.Configure(TargetApplication.ProcessName);
        }
        NotifyTargetChanged();
        return true;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        InvokeOnCaptureThreadAsync(() => StartCore(cancellationToken));

    private void StartCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stage = "refresh_target";
        try
        {
            if (!RefreshVrChatTargetCore())
                throw new InvalidOperationException("VRChat is not running or its main window is unavailable.");
            lock (_sync)
            {
                if (_running) return;
                if (_item is null)
                    throw new InvalidOperationException("VRChat is not running or its main window is unavailable.");
                if (!IsSupported)
                    throw new PlatformNotSupportedException("当前系统不支持 Windows Graphics Capture");

                stage = "create_device";
                _device = Direct3DDeviceFactory.Create();
                stage = "create_frame_pool";
                _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _device,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _item.Size);
                _pool.FrameArrived += OnFrameArrived;
                stage = "create_capture_session";
                _session = _pool.CreateCaptureSession(_item);
                stage = "configure_capture_session";
                _session.IsCursorCaptureEnabled = false;
                _statistics.Reset();
                _running = true;
                stage = "start_frame_source";
                source.StartAsync(cancellationToken).GetAwaiter().GetResult();
                stage = "start_capture";
                _session.StartCapture();
            }
            logger.LogInformation(
                "WGC capture started thread_id={ThreadId} window=0x{Window:X}",
                Environment.CurrentManagedThreadId,
                _targetWindow.ToInt64());
        }
        catch (Exception error)
        {
            LogWinRtFailure(stage, error);
            try { StopCore(CancellationToken.None, logStatistics: false); }
            catch (Exception cleanupError)
            {
                LogWinRtFailure("startup_cleanup", cleanupError);
            }
            throw new InvalidOperationException($"WGC 启动失败，阶段：{stage}", error);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        InvokeOnCaptureThreadAsync(() => StopCore(cancellationToken, logStatistics: true));

    private void StopCore(CancellationToken cancellationToken, bool logStatistics)
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
        Exception? failure = null;
        TryCleanup("unsubscribe_frame_callback", () =>
        {
            if (pool is not null) pool.FrameArrived -= OnFrameArrived;
        });
        TryCleanup("dispose_capture_session", () => session?.Dispose());
        TryCleanup("dispose_frame_pool", () => pool?.Dispose());
        TryCleanup("wait_for_frame_readback", () =>
        {
            _frameGate.Wait(cancellationToken);
            _frameGate.Release();
        });
        TryCleanup("dispose_capture_device", () => device?.Dispose());
        TryCleanup("stop_frame_source", () =>
            source.StopAsync(cancellationToken).GetAwaiter().GetResult());
        _readbackGate.Cancel();
        if (logStatistics)
        {
            var statistics = _statistics.Snapshot();
            logger.LogInformation(
                "WGC capture stopped thread_id={ThreadId} received={Received} skipped={Skipped} readbacks={Readbacks} readback_avg_ms={Average:F2} readback_p95_ms={P95:F2}",
                Environment.CurrentManagedThreadId,
                statistics.Received,
                statistics.Skipped,
                statistics.Readbacks,
                statistics.AverageMilliseconds,
                statistics.P95Milliseconds);
        }
        if (failure is not null)
            throw new InvalidOperationException("WGC 停止时未能释放全部捕获资源", failure);

        void TryCleanup(string stage, Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                failure ??= error;
                LogWinRtFailure(stage, error);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync(CancellationToken.None);
        await InvokeOnCaptureThreadAsync(ClearTargetCore);
        await _captureThread.ShutdownQueueAsync();
        _frameGate.Dispose();
        await source.DisposeAsync();
    }

    private void ClearTargetCore()
    {
        GraphicsCaptureItem? previous;
        lock (_sync)
        {
            if (_item is null && _targetWindow == IntPtr.Zero) return;
            previous = _item;
            if (previous is not null && _itemClosedHandler is not null)
                previous.Closed -= _itemClosedHandler;
            _item = null;
            _itemClosedHandler = null;
            _targetWindow = IntPtr.Zero;
        }
        NotifyTargetChanged();
    }

    private void HandleTargetClosed(GraphicsCaptureItem closedItem)
    {
        var wasRunning = false;
        lock (_sync)
        {
            if (!ReferenceEquals(_item, closedItem)) return;
            wasRunning = _running;
            if (_itemClosedHandler is not null)
                _item.Closed -= _itemClosedHandler;
            _item = null;
            _itemClosedHandler = null;
            _targetWindow = IntPtr.Zero;
        }
        NotifyTargetChanged();
        if (wasRunning)
            source.PublishCaptureFailure(new InvalidOperationException("VRChat 捕获窗口已关闭"));
    }

    private T InvokeOnCaptureThread<T>(Func<T> action) =>
        InvokeOnCaptureThreadAsync(action).GetAwaiter().GetResult();

    private Task InvokeOnCaptureThreadAsync(Action action) =>
        InvokeOnCaptureThreadAsync(() =>
        {
            action();
            return true;
        });

    private Task<T> InvokeOnCaptureThreadAsync<T>(Func<T> action)
    {
        if (_captureThread.DispatcherQueue.HasThreadAccess)
        {
            try { return Task.FromResult(action()); }
            catch (Exception error) { return Task.FromException<T>(error); }
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_captureThread.DispatcherQueue.TryEnqueue(() =>
            {
                try { completion.TrySetResult(action()); }
                catch (Exception error) { completion.TrySetException(error); }
            }))
        {
            completion.TrySetException(new ObjectDisposedException(nameof(WgcCaptureAdapter)));
        }
        return completion.Task;
    }

    private void PostToCaptureThread(Action action)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!_captureThread.DispatcherQueue.TryEnqueue(() =>
            {
                try { action(); }
                catch (Exception error) { LogWinRtFailure("capture_thread_callback", error); }
            }))
        {
            logger.LogWarning("WGC capture-thread callback was rejected because the dispatcher is stopping");
        }
    }

    private void LogWinRtFailure(string stage, Exception error) =>
        logger.LogError(
            error,
            "WGC operation failed stage={Stage} thread_id={ThreadId} hresult=0x{HResult:X8}",
            stage,
            Environment.CurrentManagedThreadId,
            unchecked((uint)error.HResult));

    private void NotifyTargetChanged()
    {
        foreach (EventHandler handler in TargetChanged?.GetInvocationList().Cast<EventHandler>() ?? [])
        {
            try { handler(this, EventArgs.Empty); }
            catch
            {
                // Target notifications run on the dedicated capture thread. UI
                // subscribers cannot be allowed to terminate the process.
            }
        }
    }

    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        var entered = false;
        Exception? failure = null;
        var stage = "acquire_frame_gate";
        try
        {
            entered = await _frameGate.WaitAsync(0);
            if (!entered) return;

            Direct3D11CaptureFrame? latest = null;
            var received = 0;
            try
            {
                while (_running)
                {
                    stage = "dequeue_frame";
                    var frame = sender.TryGetNextFrame();
                    if (frame is null) break;
                    received++;
                    latest?.Dispose();
                    latest = frame;
                }

                if (latest is null) return;
                _statistics.RecordReceived(received);
                if (!_readbackGate.TryClaim())
                {
                    _statistics.RecordSkipped(received);
                    return;
                }

                _statistics.RecordSkipped(Math.Max(0, received - 1));
                var capturedAt = DateTimeOffset.UtcNow;
                var capturedTimestamp = Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());
                var readbackStarted = Stopwatch.GetTimestamp();
                stage = "copy_surface";
                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                    latest.Surface,
                    BitmapAlphaMode.Ignore);
                stage = "copy_bgra_buffer";
                var pixels = CopyBgra(bitmap);
                _statistics.RecordReadback(Stopwatch.GetElapsedTime(readbackStarted).TotalMilliseconds);
                source.PublishCapturedFrame(
                    pixels,
                    bitmap.PixelWidth,
                    bitmap.PixelHeight,
                    capturedAt,
                    capturedTimestamp);
            }
            finally
            {
                latest?.Dispose();
            }
        }
        catch (Exception) when (!_running)
        {
            // Closing a frame pool may race with its final event.
        }
        catch (Exception error)
        {
            LogWinRtFailure(stage, error);
            lock (_sync)
            {
                if (_running)
                {
                    _running = false;
                    failure = new InvalidOperationException(
                        $"VRChat 画面捕获失败，阶段：{stage}",
                        error);
                }
            }
        }
        finally
        {
            if (entered) _frameGate.Release();
        }

        // Runtime rollback may stop the frame pool, so notify only after the
        // callback no longer owns the frame gate.
        if (failure is not null)
            source.PublishCaptureFailure(failure);
    }

    private ReadOnlyMemory<byte> CopyBgra(SoftwareBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"捕获帧尺寸无效：{width}x{height}");
        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
            throw new InvalidDataException($"捕获帧像素格式必须为 BGRA8，实际为 {bitmap.BitmapPixelFormat}");

        var byteLength = checked(width * height * 4);
        if (_pixelBuffer is null || _pixelBuffer.Capacity != (uint)byteLength)
        {
            _pixelBuffer = new Windows.Storage.Streams.Buffer((uint)byteLength);
            _pixelBytes = new byte[byteLength];
        }

        bitmap.CopyToBuffer(_pixelBuffer);
        if (_pixelBuffer.Length != (uint)byteLength)
            throw new InvalidDataException(
                $"捕获帧字节数无效：需要 {byteLength}，实际为 {_pixelBuffer.Length}");

        using var reader = DataReader.FromBuffer(_pixelBuffer);
        if (reader.UnconsumedBufferLength != (uint)byteLength)
            throw new InvalidDataException(
                $"捕获帧可读字节数无效：需要 {byteLength}，实际为 {reader.UnconsumedBufferLength}");
        reader.ReadBytes(_pixelBytes!);
        return _pixelBytes.AsMemory(0, byteLength);
    }
}

internal sealed class CaptureReadbackStatistics
{
    private const int Capacity = 512;
    private readonly object _sync = new();
    private readonly double[] _samples = new double[Capacity];
    private long _received;
    private long _skipped;
    private long _readbacks;
    private int _sampleCount;
    private int _nextSample;

    public void Reset()
    {
        lock (_sync)
        {
            _received = 0;
            _skipped = 0;
            _readbacks = 0;
            _sampleCount = 0;
            _nextSample = 0;
            Array.Clear(_samples);
        }
    }

    public void RecordReceived(int count) => Interlocked.Add(ref _received, count);
    public void RecordSkipped(int count) => Interlocked.Add(ref _skipped, count);

    public void RecordReadback(double milliseconds)
    {
        Interlocked.Increment(ref _readbacks);
        lock (_sync)
        {
            _samples[_nextSample] = Math.Max(0, milliseconds);
            _nextSample = (_nextSample + 1) % Capacity;
            _sampleCount = Math.Min(Capacity, _sampleCount + 1);
        }
    }

    public CaptureReadbackSnapshot Snapshot()
    {
        lock (_sync)
        {
            if (_sampleCount == 0)
                return new(
                    Interlocked.Read(ref _received),
                    Interlocked.Read(ref _skipped),
                    Interlocked.Read(ref _readbacks),
                    0,
                    0);

            var samples = _samples.AsSpan(0, _sampleCount).ToArray();
            Array.Sort(samples);
            var average = samples.Average();
            var p95Index = Math.Clamp((int)Math.Ceiling(samples.Length * 0.95) - 1, 0, samples.Length - 1);
            return new(
                Interlocked.Read(ref _received),
                Interlocked.Read(ref _skipped),
                Interlocked.Read(ref _readbacks),
                average,
                samples[p95Index]);
        }
    }
}

internal readonly record struct CaptureReadbackSnapshot(
    long Received,
    long Skipped,
    long Readbacks,
    double AverageMilliseconds,
    double P95Milliseconds);

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
