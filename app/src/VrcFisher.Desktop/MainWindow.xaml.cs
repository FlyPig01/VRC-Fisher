using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcFisher.Core;
using VrcFisher.Application;
using Windows.Graphics.Capture;

namespace VrcFisher.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly IRuntimeController _runtime;
    private readonly IModelCatalog _models;
    private readonly DirectoryLayout _layout;
    private readonly WgcCaptureAdapter _capture;
    private readonly Func<AppOptions, Task> _saveOptions;
    private AppOptions _options;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow(
        IRuntimeController runtime,
        IModelCatalog models,
        DirectoryLayout layout,
        WgcCaptureAdapter capture,
        AppOptions options,
        Func<AppOptions, Task> saveOptions)
    {
        InitializeComponent();
        ApplyLocalizedChrome();
        _runtime = runtime;
        _models = models;
        _layout = layout;
        _capture = capture;
        _saveOptions = saveOptions;
        _options = options;
        Title = "VRC-Fisher";
        Navigation.SelectedItem = Navigation.MenuItems[0];
        if (ContentFrame.Content is null) ShowPage("run");
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
    }

    public IRuntimeController Runtime => _runtime;
    public IModelCatalog Models => _models;
    public DirectoryLayout Layout => _layout;
    public WgcCaptureAdapter Capture => _capture;
    public AppOptions Options => _options;

    public async Task SelectCaptureTargetAsync()
    {
        var picker = new GraphicsCapturePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var item = await picker.PickSingleItemAsync();
        if (item is null) return;
        _capture.Configure(item);
        _options = _options with { CaptureDisplay = _capture.TargetName };
        await _saveOptions(_options);
    }

    public async Task SaveOptionsAsync(AppOptions options)
    {
        _options = options;
        await _saveOptions(options);
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        ShowPage(item.Tag?.ToString());
    }

    private void ShowPage(string? pageName)
    {
        var page = pageName switch
        {
            "models" => new ModelsPage(),
            "settings" => new SettingsPage(),
            "diagnostics" => new DiagnosticsPage(),
            _ => (Page)new RunPage()
        };
        page.Tag = this;
        ContentFrame.Content = page;
    }

    private void RefreshStatus()
    {
        var snapshot = _runtime.Snapshot;
        StatusBadge.Text = _models.IsReady ? UiStrings.Phase(snapshot.Phase) : UiStrings.Get("ModelsNotReady");
    }

    private void ApplyLocalizedChrome()
    {
        AppTitleText.Text = UiStrings.Get("AppTitle");
        RunNavigationItem.Content = UiStrings.Get("Run");
        ModelsNavigationItem.Content = UiStrings.Get("Models");
        SettingsNavigationItem.Content = UiStrings.Get("Settings");
        DiagnosticsNavigationItem.Content = UiStrings.Get("Diagnostics");
        StatusBadge.Text = UiStrings.Get("ModelsNotReady");
    }
}
