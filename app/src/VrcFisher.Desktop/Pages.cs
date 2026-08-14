using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Infrastructure.Inference;

namespace VrcFisher.Desktop;

public sealed class RunPage : Page
{
    private TextBlock _status = null!;
    private TextBlock _provider = null!;
    private TextBlock _performance = null!;
    private InfoBar _performanceWarning = null!;
    private Button _observe = null!;
    private Button _automatic = null!;
    private Button _stop = null!;

    public RunPage()
    {
        var root = new StackPanel { Spacing = 16 };
        root.Children.Add(new TextBlock { Text = UiStrings.Get("Run"), FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _status = new TextBlock { Text = UiStrings.Get("StatusLoading"), TextWrapping = TextWrapping.Wrap };
        _provider = new TextBlock { Text = UiStrings.Get("ProviderUnavailable") };
        _performance = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _performanceWarning = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            Severity = InfoBarSeverity.Warning,
            Title = UiStrings.Get("PerformanceInsufficientTitle"),
            Message = UiStrings.Get("PerformanceInsufficientMessage")
        };
        root.Children.Add(_status);
        root.Children.Add(_provider);
        root.Children.Add(_performance);
        root.Children.Add(_performanceWarning);
        _observe = new Button { Content = UiStrings.Get("Observe"), HorizontalAlignment = HorizontalAlignment.Left };
        _automatic = new Button { Content = UiStrings.Get("Automatic"), HorizontalAlignment = HorizontalAlignment.Left };
        _stop = new Button { Content = UiStrings.Get("Stop"), HorizontalAlignment = HorizontalAlignment.Left };
        _observe.Click += async (_, _) => await StartAsync(false);
        _automatic.Click += async (_, _) => await StartAsync(true);
        _stop.Click += async (_, _) => await StopAsync();
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _observe, _automatic, _stop } });
        Content = root;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private MainWindow Window => (MainWindow)Tag!;

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Window.Runtime.SnapshotChanged += OnSnapshotChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (Tag is MainWindow window) window.Runtime.SnapshotChanged -= OnSnapshotChanged;
    }

    private void OnSnapshotChanged(object? sender, RuntimeSnapshot snapshot) =>
        DispatcherQueue.TryEnqueue(Refresh);

    private async Task StartAsync(bool automatic)
    {
        await Window.Runtime.StartObservationAsync(automatic, CancellationToken.None);
        Refresh();
    }

    private async Task StopAsync()
    {
        await Window.Runtime.StopAsync(CancellationToken.None);
        Refresh();
    }

    private void Refresh()
    {
        var snapshot = Window.Runtime.Snapshot;
        _status.Text = UiStrings.Format("RuntimeStatus", UiStrings.Phase(snapshot.Phase), UiStrings.RuntimeStatus(snapshot.Status));
        _provider.Text = UiStrings.Format("RuntimeProvider", UiStrings.Provider(snapshot.Provider),
            snapshot.ModelsReady ? UiStrings.Get("Ready") : UiStrings.Get("ModelsNotReady"));
        _performance.Text = UiStrings.Performance(snapshot.Performance);
        _performanceWarning.IsOpen = snapshot.Performance.PerformanceInsufficient;
        var canStart = Window.Models.IsReady && Window.Capture.IsConfigured && !snapshot.IsObserving;
        _observe.IsEnabled = canStart;
        _automatic.IsEnabled = canStart && Window.Models.AutomaticAllowed;
        _stop.IsEnabled = snapshot.IsObserving;
    }
}

public sealed class ModelsPage : Page
{
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
    private CancellationTokenSource? _downloadCancellation;
    private MainWindow _window = null!;

    public ModelsPage()
    {
        var root = new StackPanel { Spacing = 16 };
        root.Children.Add(new TextBlock { Text = UiStrings.Get("Models"), FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = UiStrings.Get("ModelsDescription"), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(_list);
        root.Children.Add(_progress);
        root.Children.Add(_message);
        var refresh = new Button { Content = UiStrings.Get("Refresh") };
        var download = new Button { Content = UiStrings.Get("DownloadModels") };
        var cancel = new Button { Content = UiStrings.Get("CancelDownload") };
        var delete = new Button { Content = UiStrings.Get("DeleteModels") };
        refresh.Click += async (_, _) => await RefreshAsync();
        download.Click += async (_, _) => await DownloadAsync();
        cancel.Click += (_, _) => _downloadCancellation?.Cancel();
        delete.Click += async (_, _) => await DeleteAsync();
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { refresh, download, cancel, delete } });
        Content = root;
        Loaded += async (_, _) => { _window = (MainWindow)Tag!; await RefreshAsync(); };
    }

    private async Task RefreshAsync()
    {
        await _window.Models.RefreshAsync(CancellationToken.None);
        _list.Children.Clear();
        foreach (var item in _window.Models.GetStatus())
        {
            var text = !item.Installed
                ? UiStrings.Format("ModelMissing", item.Name)
                : item.Valid
                    ? UiStrings.Format("ModelValid", item.Name, item.Version ?? "-", item.Size / 1048576d)
                    : UiStrings.Format("ModelInvalid", item.Name, item.Size / 1048576d);
            _list.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        }
    }

    private async Task DownloadAsync()
    {
        _downloadCancellation?.Dispose();
        _downloadCancellation = new CancellationTokenSource();
        _progress.Visibility = Visibility.Visible;
        _message.Text = UiStrings.Get("CheckingModels");
        try
        {
            var progress = new Progress<ModelDownloadProgress>(value =>
            {
                _progress.Value = value.BytesTotal <= 0 ? 0 : (double)value.BytesDownloaded / value.BytesTotal;
                _message.Text = UiStrings.Format("DownloadProgress", value.CurrentFile, value.BytesDownloaded, value.BytesTotal);
            });
            await _window.Models.DownloadLatestAsync(progress, _downloadCancellation.Token);
            _message.Text = UiStrings.Get("DownloadComplete");
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            _message.Text = UiStrings.Get("DownloadCancelled");
        }
        catch (Exception error)
        {
            _message.Text = UiStrings.Format("DownloadFailed", error.Message);
        }
        finally
        {
            _progress.Visibility = Visibility.Collapsed;
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
        }
    }

    private async Task DeleteAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiStrings.Get("ConfirmDeleteTitle"),
            Content = UiStrings.Get("ConfirmDeleteModels"),
            PrimaryButtonText = UiStrings.Get("DeleteModels"),
            CloseButtonText = UiStrings.Get("Cancel")
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await _window.Models.DeleteModelsAsync(CancellationToken.None);
        await RefreshAsync();
    }
}

public sealed class SettingsPage : Page
{
    private TextBlock _captureStatus = null!;
    private ComboBox _language = null!;
    private ComboBox _device = null!;
    private Slider _biteFallback = null!;
    private TextBlock _biteFallbackValue = null!;
    private ToggleSwitch _adaptiveInference = null!;
    private StackPanel _manualFrequencyPanel = null!;
    private NumberBox _locatorInterval = null!;
    private NumberBox _hookingInterval = null!;
    private NumberBox _minigameInterval = null!;
    private NumberBox _panelRecheckInterval = null!;
    private bool _refreshing;

    public SettingsPage()
    {
        _language = new ComboBox { Width = 180 };
        _language.Items.Add(new ComboBoxItem { Content = UiStrings.Get("LanguageChinese"), Tag = "zh-CN" });
        _language.Items.Add(new ComboBoxItem { Content = UiStrings.Get("LanguageEnglish"), Tag = "en-US" });
        _language.SelectionChanged += async (_, _) =>
        {
            if (_refreshing || Tag is not MainWindow window || _language.SelectedItem is not ComboBoxItem item || item.Tag is not string language)
                return;
            await window.ChangeLanguageAsync(language);
        };
        var select = new Button { Content = UiStrings.Get("SelectCapture") };
        select.Click += async (_, _) =>
        {
            if (Tag is not MainWindow window) return;
            await window.SelectCaptureTargetAsync();
            Refresh(window);
        };
        _captureStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var devices = OnnxRuntimeDetector.SupportsDirectML
            ? new[] { "Auto", "CPU", "GPU" }
            : new[] { "Auto", "CPU" };
        _device = new ComboBox { ItemsSource = devices, Width = 180 };
        _device.SelectionChanged += async (_, _) =>
        {
            if (Tag is not MainWindow window || _device.SelectedItem is not string value) return;
            var device = value switch
            {
                "CPU" => VrcFisher.Core.ExecutionDevice.Cpu,
                "GPU" => VrcFisher.Core.ExecutionDevice.Gpu,
                _ => VrcFisher.Core.ExecutionDevice.Auto
            };
            await window.SaveOptionsAsync(window.Options with { Device = device });
        };
        _biteFallbackValue = new TextBlock();
        _biteFallback = new Slider
        {
            Minimum = 0,
            Maximum = 20,
            StepFrequency = 0.5,
            Width = 300,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _biteFallback.ValueChanged += async (_, _) =>
        {
            _biteFallbackValue.Text = FormatBiteFallback(_biteFallback.Value);
            if (_refreshing || Tag is not MainWindow window) return;
            await window.SaveOptionsAsync(window.Options with { BiteFallbackSeconds = _biteFallback.Value });
        };
        _adaptiveInference = new ToggleSwitch
        {
            Header = UiStrings.Get("AdaptiveInference"),
            OnContent = UiStrings.Get("Enabled"),
            OffContent = UiStrings.Get("Disabled")
        };
        _adaptiveInference.Toggled += async (_, _) =>
        {
            _manualFrequencyPanel.Visibility = _adaptiveInference.IsOn
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (_refreshing || Tag is not MainWindow window) return;
            await window.SaveOptionsAsync(window.Options with
            {
                AdaptiveInference = _adaptiveInference.IsOn
            });
        };
        _locatorInterval = CreateIntervalBox("LocatorInterval", 80, 250, 10);
        _hookingInterval = CreateIntervalBox("HookingInterval", 80, 250, 10);
        _minigameInterval = CreateIntervalBox("MinigameInterval", 33, 67, 1);
        _panelRecheckInterval = CreateIntervalBox("PanelRecheckInterval", 250, 1000, 50);
        _locatorInterval.ValueChanged += SaveManualFrequencies;
        _hookingInterval.ValueChanged += SaveManualFrequencies;
        _minigameInterval.ValueChanged += SaveManualFrequencies;
        _panelRecheckInterval.ValueChanged += SaveManualFrequencies;
        _manualFrequencyPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = UiStrings.Get("ManualFrequencyDescription"),
                    TextWrapping = TextWrapping.Wrap
                },
                _locatorInterval,
                _hookingInterval,
                _minigameInterval,
                _panelRecheckInterval
            }
        };
        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = UiStrings.Get("Settings"), FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                new TextBlock { Text = UiStrings.Get("Language") },
                _language,
                select,
                _captureStatus,
                new TextBlock { Text = UiStrings.Get("Device") },
                _device,
                new TextBlock { Text = UiStrings.Get("BiteFallback") },
                _biteFallback,
                _biteFallbackValue,
                _adaptiveInference,
                new TextBlock
                {
                    Text = UiStrings.Get("AdaptiveInferenceDescription"),
                    TextWrapping = TextWrapping.Wrap
                },
                _manualFrequencyPanel,
                new TextBlock { Text = UiStrings.Get("SoftwareRoot") },
                new TextBlock { Text = "", TextWrapping = TextWrapping.Wrap }
            }
        };
        Loaded += (_, _) => { if (Tag is MainWindow window) Refresh(window); };
    }

    private void Refresh(MainWindow window)
    {
        _refreshing = true;
        _captureStatus.Text = window.Capture.IsConfigured
            ? UiStrings.Format("CaptureTarget", window.Capture.TargetName)
            : UiStrings.Get("CaptureNotSelected");
        _language.SelectedIndex = window.Options.Language == "en-US" ? 1 : 0;
        _device.SelectedItem = window.Options.Device switch
        {
            VrcFisher.Core.ExecutionDevice.Cpu => "CPU",
            VrcFisher.Core.ExecutionDevice.Gpu => "GPU",
            _ => "Auto"
        };
        _biteFallback.Value = window.Options.BiteFallbackSeconds;
        _biteFallbackValue.Text = FormatBiteFallback(_biteFallback.Value);
        _adaptiveInference.IsOn = window.Options.AdaptiveInference;
        _manualFrequencyPanel.Visibility = window.Options.AdaptiveInference
            ? Visibility.Collapsed
            : Visibility.Visible;
        _locatorInterval.Value = window.Options.LocatorIntervalMs;
        _hookingInterval.Value = window.Options.HookingIntervalMs;
        _minigameInterval.Value = window.Options.MinigameIntervalMs;
        _panelRecheckInterval.Value = window.Options.PanelRecheckIntervalMs;
        if (Content is StackPanel panel && panel.Children.LastOrDefault() is TextBlock rootText)
            rootText.Text = window.Layout.Root;
        _refreshing = false;
    }

    private static string FormatBiteFallback(double seconds) => seconds <= 0
        ? UiStrings.Get("BiteFallbackDisabled")
        : UiStrings.Format("BiteFallbackSeconds", seconds);

    private static NumberBox CreateIntervalBox(
        string headerKey,
        double minimum,
        double maximum,
        double step) => new()
    {
        Header = UiStrings.Get(headerKey),
        Minimum = minimum,
        Maximum = maximum,
        SmallChange = step,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        Width = 300,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private async void SaveManualFrequencies(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_refreshing || Tag is not MainWindow window) return;
        if (!double.IsFinite(_locatorInterval.Value)
            || !double.IsFinite(_hookingInterval.Value)
            || !double.IsFinite(_minigameInterval.Value)
            || !double.IsFinite(_panelRecheckInterval.Value))
        {
            return;
        }
        await window.SaveOptionsAsync(window.Options with
        {
            LocatorIntervalMs = (int)_locatorInterval.Value,
            HookingIntervalMs = (int)_hookingInterval.Value,
            MinigameIntervalMs = (int)_minigameInterval.Value,
            PanelRecheckIntervalMs = (int)_panelRecheckInterval.Value
        });
    }
}

public sealed class DiagnosticsPage : Page
{
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    public DiagnosticsPage()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = UiStrings.Get("Diagnostics"), FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                _status,
                new TextBlock { Text = UiStrings.Get("DiagnosticsDescription"), TextWrapping = TextWrapping.Wrap }
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (Tag is not MainWindow window) return;
        window.Runtime.SnapshotChanged += OnSnapshotChanged;
        Refresh(window.Runtime.Snapshot);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (Tag is MainWindow window) window.Runtime.SnapshotChanged -= OnSnapshotChanged;
    }

    private void OnSnapshotChanged(object? sender, RuntimeSnapshot snapshot) =>
        DispatcherQueue.TryEnqueue(() => Refresh(snapshot));

    private void Refresh(RuntimeSnapshot snapshot)
    {
        _status.Text = UiStrings.Format(
            "DiagnosticsStatus",
            UiStrings.Provider(snapshot.Provider),
            snapshot.FramesCaptured,
            snapshot.FramesDropped,
            UiStrings.RuntimeStatus(snapshot.Status),
            UiStrings.Performance(snapshot.Performance));
    }
}
