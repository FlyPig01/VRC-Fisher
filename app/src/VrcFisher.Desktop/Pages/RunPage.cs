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
    private readonly TextBlock _physicalCoresValue = NumericValueText();
    private readonly TextBlock _logicalThreadsValue = NumericValueText();
    private readonly Grid _graphicsGrid = new() { ColumnSpacing = 20, RowSpacing = 12 };
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

        var processorGrid = CreateProcessorGrid();
        InitializeGraphicsGrid([]);
        var systemGrid = CreateInfoGrid(
            (UiStrings.Get("Memory"), _memoryValue),
            (UiStrings.Get("System"), _systemValue));
        var inferenceGrid = CreateInfoGrid(
            (UiStrings.Get("SelectedDevice"), _selectedDeviceValue),
            (UiStrings.Get("ActualDevice"), _actualDeviceValue));

        var root = UiFactory.PageStack();
        root.Children.Add(UiFactory.Surface(statusStack));
        root.Children.Add(UiFactory.Section(UiStrings.Get("Readiness"), readinessGrid));
        root.Children.Add(_readinessInfo);
        root.Children.Add(_performanceWarning);
        root.Children.Add(UiFactory.Section(UiStrings.Get("Cpu"), processorGrid));
        root.Children.Add(UiFactory.Section(UiStrings.Get("Gpu"), _graphicsGrid));
        root.Children.Add(UiFactory.Section(UiStrings.Get("System"), systemGrid));
        root.Children.Add(UiFactory.Section(UiStrings.Get("ExecutionProvider"), inferenceGrid));
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
        _cpuValue.Text = hardware.CpuName == "Unavailable"
            ? UiStrings.Get("Unavailable")
            : hardware.CpuName;
        _physicalCoresValue.Text = hardware.PhysicalCores.ToString();
        _logicalThreadsValue.Text = hardware.LogicalProcessors.ToString();
        InitializeGraphicsGrid(hardware.GraphicsAdapters);
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
        var lastExecution = FormatLastExecution(snapshot);
        _executionValue.Text = lastExecution;
        _selectedDeviceValue.Text = UiStrings.Device(_context.Options.Device);
        _actualDeviceValue.Text = lastExecution;

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

    private string FormatLastExecution(RuntimeSnapshot snapshot)
    {
        var execution = snapshot.LastSuccessfulExecution;
        return ExecutionRuntimeInfo.GetHistoryState(execution, _context.Options.Device) switch
        {
            ExecutionHistoryState.NoRun => UiStrings.Get("NoRunHistory"),
            ExecutionHistoryState.AwaitingConfirmation => UiStrings.Get("AwaitingNextRunConfirmation"),
            _ => FormatExecution(execution!)
        };
    }

    private static TextBlock ValueText() => new()
    {
        FontSize = 16,
        TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock NumericValueText() => new()
    {
        FontSize = 16,
        TextAlignment = TextAlignment.Right,
        HorizontalAlignment = HorizontalAlignment.Stretch
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

    private static Grid CreateInfoGrid(params (string Label, TextBlock Value)[] rows)
    {
        var grid = new Grid { ColumnSpacing = 24, RowSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < rows.Length; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddHardwareRow(grid, index, rows[index].Label, rows[index].Value);
        }
        return grid;
    }

    private Grid CreateProcessorGrid()
    {
        var grid = CreateTable(
            ("CPU", new GridLength(1, GridUnitType.Star), TextAlignment.Left),
            ("Cores", new GridLength(160), TextAlignment.Right),
            ("Threads", new GridLength(160), TextAlignment.Right));
        AddTableValue(grid, 1, 0, _cpuValue);
        AddTableValue(grid, 1, 1, _physicalCoresValue);
        AddTableValue(grid, 1, 2, _logicalThreadsValue);
        return grid;
    }

    private void InitializeGraphicsGrid(IReadOnlyList<GraphicsAdapterInfo> adapters)
    {
        _graphicsGrid.Children.Clear();
        _graphicsGrid.ColumnDefinitions.Clear();
        _graphicsGrid.RowDefinitions.Clear();
        ConfigureTable(
            _graphicsGrid,
            ("#", new GridLength(44), TextAlignment.Right),
            ("GPU", new GridLength(1, GridUnitType.Star), TextAlignment.Left),
            ("Memory", new GridLength(112), TextAlignment.Right),
            ("Driver", new GridLength(180), TextAlignment.Right));

        if (adapters.Count == 0)
        {
            _graphicsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var unavailable = ValueText();
            unavailable.Text = UiStrings.Get("NoGraphicsAdapter");
            AddTableValue(_graphicsGrid, 1, 0, unavailable);
            Grid.SetColumnSpan(unavailable, 4);
            return;
        }

        for (var index = 0; index < adapters.Count; index++)
        {
            var adapter = adapters[index];
            var row = index + 1;
            _graphicsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddTableValue(_graphicsGrid, row, 0, TableValue($"#{adapter.Index}", TextAlignment.Right));
            AddTableValue(_graphicsGrid, row, 1, TableValue(adapter.Name));
            AddTableValue(_graphicsGrid, row, 2, TableValue(
                adapter.DedicatedMemoryBytes > 0
                    ? DataSizeFormatter.Format(adapter.DedicatedMemoryBytes)
                    : UiStrings.Get("Unavailable"),
                TextAlignment.Right));
            AddTableValue(_graphicsGrid, row, 3, TableValue(
                adapter.DriverVersion ?? UiStrings.Get("Unavailable"),
                TextAlignment.Right));
        }
    }

    private static Grid CreateTable(
        params (string Header, GridLength Width, TextAlignment Alignment)[] columns)
    {
        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 12 };
        ConfigureTable(grid, columns);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    private static void ConfigureTable(
        Grid grid,
        params (string Header, GridLength Width, TextAlignment Alignment)[] columns)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < columns.Length; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = columns[index].Width });
            var header = UiFactory.Secondary(columns[index].Header);
            header.TextAlignment = columns[index].Alignment;
            header.HorizontalAlignment = HorizontalAlignment.Stretch;
            AddTableValue(grid, 0, index, header);
        }
    }

    private static TextBlock TableValue(string text, TextAlignment alignment = TextAlignment.Left) => new()
    {
        Text = text,
        FontSize = 16,
        TextAlignment = alignment,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static void AddTableValue(Grid grid, int row, int column, FrameworkElement value)
    {
        Grid.SetRow(value, row);
        Grid.SetColumn(value, column);
        grid.Children.Add(value);
    }
}
