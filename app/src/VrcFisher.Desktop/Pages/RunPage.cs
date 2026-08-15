using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcFisher.Core;
using VrcFisher.Desktop.Contracts;
using VrcFisher.Desktop.Localization;
using VrcFisher.Desktop.Ui;

namespace VrcFisher.Desktop.Pages;

internal sealed class RunPage : Page
{
    private readonly IDesktopPageContext _context;
    private readonly TextBlock _phaseValue = new()
    {
        FontSize = 24,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
    };
    private readonly TextBlock _statusDetail = UiFactory.Secondary();
    private readonly TextBlock _captureValue = new() { FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _modelsValue = new() { FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _providerValue = new() { FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private readonly InfoBar _readinessInfo = new()
    {
        IsClosable = false,
        Severity = InfoBarSeverity.Informational
    };
    private readonly InfoBar _performanceWarning = new()
    {
        IsClosable = false,
        Severity = InfoBarSeverity.Warning,
        Title = UiStrings.Get("PerformanceInsufficientTitle"),
        Message = UiStrings.Get("PerformanceInsufficientMessage")
    };
    public RunPage(IDesktopPageContext context)
    {
        _context = context;
        var statusStack = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                UiFactory.Secondary(UiStrings.Get("CurrentStatus")),
                _phaseValue,
                _statusDetail
            }
        };

        var readinessGrid = new Grid { ColumnSpacing = 28 };
        readinessGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        readinessGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        readinessGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddStatusCell(readinessGrid, 0, UiStrings.Get("CaptureSource"), _captureValue);
        AddStatusCell(readinessGrid, 1, UiStrings.Get("ModelFiles"), _modelsValue);
        AddStatusCell(readinessGrid, 2, UiStrings.Get("ExecutionProvider"), _providerValue);

        var root = UiFactory.PageStack();
        root.Children.Add(UiFactory.PageTitle(UiStrings.Get("Run")));
        root.Children.Add(UiFactory.Surface(statusStack));
        root.Children.Add(UiFactory.Section(UiStrings.Get("Readiness"), readinessGrid));
        root.Children.Add(_readinessInfo);
        root.Children.Add(_performanceWarning);
        Content = UiFactory.Scrollable(root);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _context.Runtime.SnapshotChanged += OnSnapshotChanged;
        _context.Capture.TargetChanged += OnCaptureTargetChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _context.Runtime.SnapshotChanged -= OnSnapshotChanged;
        _context.Capture.TargetChanged -= OnCaptureTargetChanged;
    }

    private void OnSnapshotChanged(object? sender, RuntimeSnapshot snapshot) =>
        DispatcherQueue.TryEnqueue(Refresh);

    private void OnCaptureTargetChanged(object? sender, EventArgs args) =>
        DispatcherQueue.TryEnqueue(Refresh);

    private void Refresh()
    {
        var snapshot = _context.Runtime.Snapshot;
        var modelsReady = _context.Models.IsReady;
        var captureReady = _context.Capture.IsConfigured;

        _phaseValue.Text = UiStrings.Phase(snapshot.Phase);
        _statusDetail.Text = UiStrings.RuntimeStatus(snapshot.Status);
        _captureValue.Text = captureReady
            ? _context.Capture.TargetName
            : UiStrings.Get("VrChatProcessNotFound");
        _modelsValue.Text = modelsReady
            ? UiStrings.Get("Ready")
            : UiStrings.Get("ModelsNotReady");
        _providerValue.Text = snapshot.Provider == "Unavailable"
            ? UiStrings.Device(_context.Options.Device)
            : UiStrings.Provider(snapshot.Provider);

        _readinessInfo.IsOpen = !modelsReady || !captureReady;
        _readinessInfo.Title = UiStrings.Get("PreparationRequiredTitle");
        _readinessInfo.Message = (modelsReady, captureReady) switch
        {
            (false, false) => UiStrings.Get("PreparationModelsAndCapture"),
            (false, true) => UiStrings.Get("PreparationModels"),
            _ => UiStrings.Get("PreparationCapture")
        };
        _performanceWarning.IsOpen = snapshot.Performance.PerformanceInsufficient;

    }

    private static void AddStatusCell(Grid grid, int column, string label, TextBlock value)
    {
        var cell = UiFactory.StatusCell(label, value);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }
}
