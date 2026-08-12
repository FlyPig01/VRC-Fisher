using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcFisher.Application;
using VrcFisher.Core;

namespace VrcFisher.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly IRuntimeController _runtime;
    private readonly IModelCatalog _models;
    private readonly DirectoryLayout _layout;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow(IRuntimeController runtime, IModelCatalog models, DirectoryLayout layout)
    {
        InitializeComponent();
        _runtime = runtime;
        _models = models;
        _layout = layout;
        Title = "VRC-Fisher";
        Navigation.SelectedItem = Navigation.MenuItems[0];
        ContentFrame.Navigate(typeof(RunPage));
        if (ContentFrame.Content is FrameworkElement firstPage) firstPage.Tag = this;
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
    }

    public IRuntimeController Runtime => _runtime;
    public IModelCatalog Models => _models;
    public DirectoryLayout Layout => _layout;

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        ContentFrame.Navigate(item.Tag?.ToString() switch
        {
            "models" => typeof(ModelsPage),
            "settings" => typeof(SettingsPage),
            "diagnostics" => typeof(DiagnosticsPage),
            _ => typeof(RunPage)
        });
        if (ContentFrame.Content is FrameworkElement page) page.Tag = this;
    }

    private void RefreshStatus()
    {
        var snapshot = _runtime.Snapshot;
        StatusBadge.Text = snapshot.ModelsReady ? snapshot.Phase.ToString() : "模型未就绪";
    }
}
