using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcFisher.Application;
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
    private readonly TextBlock _captureValue = ValueText();
    private readonly TextBlock _modelsValue = ValueText();
    private readonly TextBlock _executionValue = ValueText();
    private readonly TextBlock _cpuValue = ValueText();
    private readonly TextBlock _gpuValue = ValueText();
    private readonly TextBlock _memoryValue = ValueText();
    private readonly TextBlock _systemValue = ValueText();
    private readonly TextBlock _selectedDeviceValue = ValueText();
    private readonly TextBlock _actualDeviceValue = ValueText();
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
        AddStatusCell(readinessGrid, 2, UiStrings.Get("ExecutionProvider"), _executionValue);

        var hardwareGrid = new Grid { ColumnSpacing = 24, RowSpacing = 16 };
        hardwareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        hardwareGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < 6; index++)
            hardwareGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddHardwareRow(hardwareGrid, 0, UiStrings.Get("Cpu"), _cpuValue);
        AddHardwareRow(hardwareGrid, 1, UiStrings.Get("Gpu"), _gpuValue);
        AddHardwareRow(hardwareGrid, 2, UiStrings.Get("Memory"), _memoryValue);
        AddHardwareRow(hardwareGrid, 3, UiStrings.Get("System"), _systemValue);
        AddHardwareRow(hardwareGrid, 4, UiStrings.Get("SelectedDevice"), _selectedDeviceValue);
        AddHardwareRow(hardwareGrid, 5, UiStrings.Get("ActualDevice"), _actualDeviceValue);

        var root = UiFactory.PageStack();
        root.Children.Add(UiFactory.PageTitle(UiStrings.Get("Run")));
        root.Children.Add(UiFactory.Surface(statusStack));
        root.Children.Add(UiFactory.Section(UiStrings.Get("Readiness"), readinessGrid));
        root.Children.Add(_readinessInfo);
        root.Children.Add(_performanceWarning);
        root.Children.Add(UiFactory.Section(UiStrings.Get("HardwareInfo"), hardwareGrid));
        Content = UiFactory.Scrollable(root);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _context.Runtime.SnapshotChanged += OnSnapshotChanged;
        _context.Capture.TargetChanged += OnCaptureTargetChanged;
        Refresh();
        _ = LoadHardwareAsync();
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

    private async Task LoadHardwareAsync()
    {
        HardwareSnapshot hardware;
        try { hardware = await _context.Hardware; }
        catch (Exception error) { hardware = HardwareSnapshot.Unavailable(error.GetBaseException().Message); }
        DispatcherQueue.TryEnqueue(() => ApplyHardware(hardware));
    }

    private void ApplyHardware(HardwareSnapshot hardware)
    {
        _cpuValue.Text = UiStrings.Format(
            "CpuTopologyFormat",
            hardware.CpuName == "Unavailable" ? UiStrings.Get("Unavailable") : hardware.CpuName,
            hardware.PhysicalCores,
            hardware.LogicalProcessors);
        _gpuValue.Text = hardware.GraphicsAdapters.Count == 0
            ? UiStrings.Get("NoGraphicsAdapter")
            : string.Join(Environment.NewLine, hardware.GraphicsAdapters.Select(adapter =>
                UiStrings.Format(
                    "GpuAdapterFormat",
                    adapter.Index,
                    adapter.Name,
                    DataSizeFormatter.Format(adapter.DedicatedMemoryBytes),
                    adapter.DriverVersion ?? UiStrings.Get("Unavailable"))));
        _memoryValue.Text = hardware.TotalMemoryBytes > 0
            ? DataSizeFormatter.Format(hardware.TotalMemoryBytes)
            : UiStrings.Get("Unavailable");
        _systemValue.Text = $"{hardware.WindowsVersion} · {(hardware.IsX64 ? "x64" : "x86")}";
    }

    private void Refresh()
    {
        var snapshot = _context.Runtime.Snapshot;
        var modelsReady = snapshot.ModelsReady;
        var captureReady = _context.Capture.IsConfigured;

        _phaseValue.Text = snapshot.Lifecycle switch
        {
            RuntimeLifecycle.Starting => UiStrings.Get("LifecycleStarting"),
            RuntimeLifecycle.Stopping => UiStrings.Get("LifecycleStopping"),
            _ => UiStrings.Phase(snapshot.Phase)
        };
        _statusDetail.Text = UiStrings.RuntimeStatus(snapshot.Status);
        _captureValue.Text = captureReady
            ? _context.Capture.TargetName
            : UiStrings.Get("VrChatProcessNotFound");
        _modelsValue.Text = modelsReady
            ? UiStrings.Get("Ready")
            : UiStrings.Get("ModelsNotReady");
        _executionValue.Text = FormatExecution(snapshot.Execution);
        _selectedDeviceValue.Text = UiStrings.Device(_context.Options.Device);
        _actualDeviceValue.Text = FormatExecution(snapshot.Execution);

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

    private string FormatExecution(ExecutionRuntimeInfo execution)
    {
        if (execution.Backend == InferenceBackend.Unavailable)
            return UiStrings.Get("Unavailable");
        var actual = execution.Backend == InferenceBackend.DirectML
            ? UiStrings.Get("DeviceGpu")
            : UiStrings.Get("DeviceCpu");
        if (!string.IsNullOrWhiteSpace(execution.DeviceName))
            actual = $"{actual} · {execution.DeviceName}";
        return execution.FellBack
            ? UiStrings.Format("FallbackToCpuFormat", actual, execution.FallbackReason ?? UiStrings.Get("UnknownError"))
            : actual;
    }

    private static TextBlock ValueText() => new()
    {
        FontSize = 16,
        TextWrapping = TextWrapping.Wrap
    };

    private static void AddStatusCell(Grid grid, int column, string label, TextBlock value)
    {
        var cell = UiFactory.StatusCell(label, value);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static void AddHardwareRow(Grid grid, int row, string label, TextBlock value)
    {
        var labelText = UiFactory.Secondary(label);
        labelText.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(labelText, row);
        grid.Children.Add(labelText);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
    }
}
