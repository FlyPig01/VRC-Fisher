using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcFisher.Core;
using VrcFisher.Application;
using VrcFisher.Desktop.Capture;
using VrcFisher.Desktop.Contracts;
using VrcFisher.Desktop.Localization;
using VrcFisher.Desktop.Pages;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Windows.Graphics;

namespace VrcFisher.Desktop;

public sealed partial class MainWindow : Window, IDesktopPageContext
{
    private readonly IRuntimeController _runtime;
    private readonly IModelCatalog _models;
    private readonly DirectoryLayout _layout;
    private readonly WgcCaptureAdapter _capture;
    private readonly Func<AppOptions, Task> _saveOptions;
    private readonly Func<string, Task> _changeHotkey;
    private readonly bool _supportsGpu;
    private readonly Task<HardwareSnapshot> _hardware;
    private AppOptions _options;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private DateTimeOffset _nextCaptureRefresh = DateTimeOffset.MinValue;

    internal MainWindow(
        IRuntimeController runtime,
        IModelCatalog models,
        DirectoryLayout layout,
        WgcCaptureAdapter capture,
        AppOptions options,
        Func<AppOptions, Task> saveOptions,
        Func<string, Task> changeHotkey,
        bool supportsGpu,
        Task<HardwareSnapshot> hardware)
    {
        InitializeComponent();
        ApplyLocalizedChrome();
        _runtime = runtime;
        _models = models;
        _layout = layout;
        _capture = capture;
        _saveOptions = saveOptions;
        _changeHotkey = changeHotkey;
        _supportsGpu = supportsGpu;
        _hardware = hardware;
        _options = options;
        Title = string.Empty;
        AppWindow.Title = string.Empty;
        AppWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VRC-Fisher.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        ResizeInitialWindow();
        _capture.RefreshVrChatTarget();
        _runtime.SnapshotChanged += OnRuntimeSnapshotChanged;
        NavigationList.SelectedItem = RunNavigationItem;
        if (ContentFrame.Content is null) ShowPage("run");
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
    }

    IRuntimeController IDesktopPageContext.Runtime => _runtime;
    IModelCatalog IDesktopPageContext.Models => _models;
    ICaptureTargetState IDesktopPageContext.Capture => _capture;
    AppOptions IDesktopPageContext.Options => _options;
    string IDesktopPageContext.SoftwareRoot => _layout.Root;
    bool IDesktopPageContext.SupportsGpu => _supportsGpu;
    Task<HardwareSnapshot> IDesktopPageContext.Hardware => _hardware;

    async Task IDesktopPageContext.SaveOptionsAsync(AppOptions options)
    {
        _options = options;
        await _saveOptions(options);
    }

    async Task IDesktopPageContext.ChangeLanguageAsync(string language)
    {
        if (!UiLanguage.Preferences.Contains(language, StringComparer.Ordinal)
            || language == _options.Language)
            return;

        _options = _options with { Language = language };
        await _saveOptions(_options);
        UiStrings.Configure(UiLanguage.Resolve(language));
        ApplyLocalizedChrome();
        var pageName = (NavigationList.SelectedItem as ListViewItem)?.Tag?.ToString() ?? "run";
        ShowPage(pageName);
    }

    async Task IDesktopPageContext.ChangeDeviceAsync(ExecutionDevice device)
    {
        if (device == ExecutionDevice.Gpu && !_supportsGpu)
            device = ExecutionDevice.Auto;
        if (device == _options.Device)
            return;

        var restart = _runtime.Snapshot.IsObserving;
        if (restart)
            await _runtime.StopAsync(CancellationToken.None);

        _options = _options with { Device = device };
        await _saveOptions(_options);

        if (restart)
            await _runtime.StartAsync(CancellationToken.None);
    }

    async Task IDesktopPageContext.ChangeHotkeyAsync(string hotkey)
    {
        if (string.Equals(hotkey, _options.ToggleHotkey, StringComparison.Ordinal)) return;
        await _changeHotkey(hotkey);
        _options = _options with { ToggleHotkey = hotkey };
    }

    void IDesktopPageContext.OpenModelsFolder() => OpenFolder(_layout.Models);

    void IDesktopPageContext.OpenSoftwareRoot() => OpenFolder(_layout.Root);

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OnNavigationChanged(object sender, SelectionChangedEventArgs args)
    {
        if (NavigationList.SelectedItem is not ListViewItem item) return;
        ShowPage(item.Tag?.ToString());
    }

    private void ShowPage(string? pageName)
    {
        ContentFrame.Content = pageName switch
        {
            "models" => new ModelsPage(this),
            "guide" => new GuidePage(this),
            "settings" => new SettingsPage(this),
            _ => (Page)new RunPage(this)
        };
    }

    private void OnRuntimeSnapshotChanged(object? sender, RuntimeSnapshot snapshot) =>
        DispatcherQueue.TryEnqueue(() => ShowRuntimeNotice(snapshot.Status, activate: false));

    internal void ShowRuntimeNotice(RuntimeStatus status, bool activate)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowRuntimeNotice(status, activate));
            return;
        }
        var notice = UiStrings.RuntimeNotice(status);
        if (notice is null)
        {
            if (status.Code is RuntimeMessageCode.AutomaticStarted or RuntimeMessageCode.Stopped)
                RuntimeNotice.IsOpen = false;
            return;
        }

        RuntimeNotice.Title = notice.Title;
        RuntimeNotice.Message = notice.Message;
        RuntimeNotice.Severity = notice.Severity == UiNoticeSeverity.Error
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Warning;
        RuntimeNotice.IsOpen = true;

        if (activate)
        {
            NavigationList.SelectedItem = RunNavigationItem;
            if (AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.Restore();
            AppWindow.Show();
            Activate();
            return;
        }

        if (notice.Severity == UiNoticeSeverity.Error)
            FlashTaskbar();
    }

    private void RefreshStatus()
    {
        var snapshot = _runtime.Snapshot;
        if (!snapshot.IsObserving && DateTimeOffset.UtcNow >= _nextCaptureRefresh)
        {
            _nextCaptureRefresh = DateTimeOffset.UtcNow.AddSeconds(1);
            _capture.RefreshVrChatTarget();
        }
        StatusBadge.Text = _models.IsReady ? UiStrings.Phase(snapshot.Phase) : UiStrings.Get("ModelsNotReady");
        StatusIcon.Symbol = _models.IsReady ? Symbol.Accept : Symbol.Important;
    }

    private void ApplyLocalizedChrome()
    {
        RunNavigationLabel.Text = UiStrings.Get("Run");
        ModelsNavigationLabel.Text = UiStrings.Get("Models");
        GuideNavigationLabel.Text = UiStrings.Get("Guide");
        SettingsNavigationLabel.Text = UiStrings.Get("Settings");
        StatusBadge.Text = UiStrings.Get("ModelsNotReady");
        StatusIcon.Symbol = Symbol.Important;
        if (RuntimeNotice.IsOpen)
            ShowRuntimeNotice(_runtime.Snapshot.Status, activate: false);
    }

    private void ResizeInitialWindow()
    {
        var scale = Math.Max(1, GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this))) / 96d;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var margin = (int)Math.Round(40 * scale);
        var width = Math.Min((int)Math.Round(1120 * scale), workArea.Width - margin);
        var height = Math.Min((int)Math.Round(720 * scale), workArea.Height - margin);
        var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    private void FlashTaskbar()
    {
        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Window = WinRT.Interop.WindowNative.GetWindowHandle(this),
            Flags = 0x00000002 | 0x0000000C,
            Count = 3,
            Timeout = 0
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);
}
