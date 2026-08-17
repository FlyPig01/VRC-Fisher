using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Desktop.Contracts;
using VrcFisher.Desktop.Localization;
using VrcFisher.Desktop.Ui;
using VrcFisher.Infrastructure.Input;
using Windows.System;

namespace VrcFisher.Desktop.Pages;

internal sealed class SettingsPage : Page
{
    private readonly IDesktopPageContext _context;
    private readonly ComboBox _language = new() { MinWidth = 220 };
    private readonly ToggleSwitch _workMode = new();
    private readonly RadioButtons _device = new()
    {
        MaxColumns = 3,
        Width = 480,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    private readonly TextBlock _hotkeyValue = new()
    {
        MinWidth = 120,
        FontSize = 16,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly ToggleSwitch _biteFallbackEnabled = new();
    private readonly Slider _biteFallback = new()
    {
        Minimum = 5,
        Maximum = 30,
        StepFrequency = 1,
        Width = 260,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform { Y = 3 }
    };
    private readonly TextBlock _biteFallbackDelayLabel = new()
    {
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _biteFallbackValue = new()
    {
        MinWidth = 92,
        VerticalAlignment = VerticalAlignment.Center
    };
    private bool _refreshing;
    private bool _confirmingHotkey;

    public SettingsPage(IDesktopPageContext context)
    {
        _context = context;

        foreach (var language in UiLanguage.Languages)
        {
            _language.Items.Add(new ComboBoxItem
            {
                Content = language.NativeName,
                Tag = language.Code
            });
        }
        _language.SelectionChanged += async (_, _) => await SaveLanguageAsync();

        _workMode.OffContent = UiStrings.Get("RunMode");
        _workMode.OnContent = UiStrings.Get("DebugMode");
        _workMode.Toggled += async (_, _) => await SaveWorkModeAsync();

        _device.Items.Add(UiStrings.Get("DeviceAuto"));
        _device.Items.Add(UiStrings.Get("DeviceCpu"));
        if (_context.SupportsGpu) _device.Items.Add(UiStrings.Get("DeviceGpu"));
        _device.SelectionChanged += async (_, _) => await SaveDeviceAsync();

        var changeHotkey = UiFactory.CommandButton(Symbol.Edit, UiStrings.Get("Change"));
        changeHotkey.Click += async (_, _) => await ChangeHotkeyAsync();
        var hotkeyControl = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Children = { _hotkeyValue, changeHotkey }
        };

        _biteFallbackEnabled.OnContent = UiStrings.Get("Enabled");
        _biteFallbackEnabled.OffContent = UiStrings.Get("Disabled");
        _biteFallbackEnabled.Toggled += async (_, _) =>
        {
            _biteFallback.IsEnabled = _biteFallbackEnabled.IsOn;
            UpdateFallbackVisualState();
            if (_refreshing) return;
            await _context.SaveOptionsAsync(_context.Options with
            {
                BiteFallbackEnabled = _biteFallbackEnabled.IsOn
            });
        };

        _biteFallback.ValueChanged += async (_, _) =>
        {
            _biteFallbackValue.Text = FormatBiteFallback(_biteFallback.Value);
            if (_refreshing) return;
            await _context.SaveOptionsAsync(_context.Options with
            {
                BiteFallbackSeconds = _biteFallback.Value
            });
        };

        var deviceRow = UiFactory.FormRow(UiStrings.Get("Device"), _device);
        ToolTipService.SetToolTip(deviceRow, UiStrings.Get("DeviceDescription"));
        var workModeRow = UiFactory.FormRow(UiStrings.Get("WorkMode"), _workMode);
        ToolTipService.SetToolTip(workModeRow, UiStrings.Get("WorkModeDescription"));
        var hotkeyRow = UiFactory.FormRow(UiStrings.Get("ToggleHotkey"), hotkeyControl);
        ToolTipService.SetToolTip(hotkeyRow, UiStrings.Get("ToggleHotkeyDescriptionLocal"));
        var general = new StackPanel
        {
            Spacing = 20,
            Children =
            {
                UiFactory.FormRow(UiStrings.Get("Language"), _language),
                workModeRow,
                deviceRow,
                hotkeyRow
            }
        };

        _biteFallbackDelayLabel.Text = UiStrings.Get("BiteFallbackDelay");
        var fallbackControl = new Grid
        {
            ColumnSpacing = 14,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        fallbackControl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        fallbackControl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fallbackControl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fallbackControl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fallbackControl.Children.Add(_biteFallbackEnabled);
        Grid.SetColumn(_biteFallbackDelayLabel, 1);
        fallbackControl.Children.Add(_biteFallbackDelayLabel);
        Grid.SetColumn(_biteFallback, 2);
        fallbackControl.Children.Add(_biteFallback);
        Grid.SetColumn(_biteFallbackValue, 3);
        fallbackControl.Children.Add(_biteFallbackValue);

        var fallbackRow = UiFactory.FormRow(UiStrings.Get("BiteFallback"), fallbackControl);
        ToolTipService.SetToolTip(fallbackRow, UiStrings.Get("BiteFallbackDescription"));
        var automationSettings = new StackPanel
        {
            Spacing = 20,
            Children = { fallbackRow }
        };

        var storagePath = new TextBlock
        {
            IsTextSelectionEnabled = true,
            Text = _context.SoftwareRoot,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(storagePath, _context.SoftwareRoot);
        var openStorage = UiFactory.CommandButton(Symbol.OpenFile, UiStrings.Get("OpenFolder"));
        openStorage.Click += (_, _) => _context.OpenSoftwareRoot();
        var storage = new Grid { ColumnSpacing = 12 };
        storage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        storage.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        storage.Children.Add(storagePath);
        Grid.SetColumn(openStorage, 1);
        storage.Children.Add(openStorage);

        var root = UiFactory.PageStack();
        root.Children.Add(UiFactory.Section(UiStrings.Get("GeneralSettings"), general));
        root.Children.Add(UiFactory.Section(UiStrings.Get("AutomationSettings"), automationSettings));
        root.Children.Add(UiFactory.Section(
            UiStrings.Get("StorageSettings"),
            UiFactory.FormRow(UiStrings.Get("SoftwareRoot"), storage)));
        Content = UiFactory.Scrollable(root);

        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        _refreshing = true;
        SelectLanguage(_context.Options.Language);
        SelectWorkMode(_context.Options.WorkMode);
        SelectDevice(_context.Options.Device);
        _hotkeyValue.Text = _context.Options.ToggleHotkey;
        _biteFallbackEnabled.IsOn = _context.Options.BiteFallbackEnabled;
        _biteFallback.Value = _context.Options.BiteFallbackSeconds;
        _biteFallback.IsEnabled = _context.Options.BiteFallbackEnabled;
        _biteFallbackValue.Text = FormatBiteFallback(_biteFallback.Value);
        UpdateFallbackVisualState();
        _refreshing = false;
    }

    private async Task ChangeHotkeyAsync()
    {
        if (_confirmingHotkey) return;
        _confirmingHotkey = true;
        try
        {
            var begin = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = UiStrings.Get("BeginHotkeyChangeTitle"),
                Content = UiStrings.Get("BeginHotkeyChangeMessage"),
                PrimaryButtonText = UiStrings.Get("Continue"),
                CloseButtonText = UiStrings.Get("Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await begin.ShowAsync() != ContentDialogResult.Primary) return;

            string? candidate = null;
            var captured = new TextBlock
            {
                Text = UiStrings.Get("CaptureHotkeyWaiting"),
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 4)
            };
            var capture = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = UiStrings.Get("CaptureHotkeyTitle"),
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = UiStrings.Get("CaptureHotkeyMessage"),
                            TextWrapping = TextWrapping.Wrap
                        },
                        captured
                    }
                },
                PrimaryButtonText = UiStrings.Get("Continue"),
                CloseButtonText = UiStrings.Get("Cancel"),
                IsPrimaryButtonEnabled = false,
                DefaultButton = ContentDialogButton.None
            };
            capture.PreviewKeyDown += (_, args) => CaptureKey(args, capture, captured, ref candidate);
            if (await capture.ShowAsync() != ContentDialogResult.Primary || candidate is null) return;

            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = UiStrings.Get("ConfirmHotkeyTitle"),
                Content = UiStrings.Format("ConfirmHotkeyMessage", _context.Options.ToggleHotkey, candidate),
                PrimaryButtonText = UiStrings.Get("ConfirmChange"),
                CloseButtonText = UiStrings.Get("Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                await _context.ChangeHotkeyAsync(candidate);
                _hotkeyValue.Text = candidate;
                var success = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = UiStrings.Get("HotkeyChangedTitle"),
                    Content = UiStrings.Format("HotkeyChangedMessage", candidate),
                    CloseButtonText = UiStrings.Get("Close")
                };
                await success.ShowAsync();
            }
            catch (Exception error)
            {
                var failure = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = UiStrings.Get("HotkeyChangeFailedTitle"),
                    Content = UiStrings.Format("HotkeyChangeFailed", candidate, error.Message),
                    CloseButtonText = UiStrings.Get("Close")
                };
                await failure.ShowAsync();
            }
        }
        finally
        {
            _confirmingHotkey = false;
        }
    }

    private static void CaptureKey(
        KeyRoutedEventArgs args,
        ContentDialog dialog,
        TextBlock captured,
        ref string? candidate)
    {
        args.Handled = true;
        if (args.Key == VirtualKey.Escape)
        {
            dialog.Hide();
            return;
        }
        try
        {
            candidate = RuntimeToggleHotkey.CaptureGesture((uint)args.Key);
            captured.Text = candidate;
            dialog.IsPrimaryButtonEnabled = true;
        }
        catch (ArgumentException)
        {
            candidate = null;
            captured.Text = UiStrings.Get("HotkeyCaptureInvalid");
            dialog.IsPrimaryButtonEnabled = false;
        }
    }

    private async Task SaveLanguageAsync()
    {
        if (_refreshing || _language.SelectedItem is not ComboBoxItem { Tag: string language }) return;
        await _context.ChangeLanguageAsync(language);
    }

    private async Task SaveDeviceAsync()
    {
        if (_refreshing || _device.SelectedIndex < 0) return;
        var device = _device.SelectedIndex switch
        {
            1 => ExecutionDevice.Cpu,
            2 when _context.SupportsGpu => ExecutionDevice.Gpu,
            _ => ExecutionDevice.Auto
        };
        await _context.ChangeDeviceAsync(device);
    }

    private async Task SaveWorkModeAsync()
    {
        if (_refreshing) return;
        var mode = _workMode.IsOn ? ApplicationMode.Debug : ApplicationMode.Run;
        await _context.SaveOptionsAsync(_context.Options with { WorkMode = mode });
    }

    private void SelectLanguage(string language)
    {
        _language.SelectedItem = _language.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, language, StringComparison.Ordinal));
        _language.SelectedIndex = Math.Max(0, _language.SelectedIndex);
    }

    private void SelectDevice(ExecutionDevice device)
    {
        _device.SelectedIndex = device switch
        {
            ExecutionDevice.Cpu => 1,
            ExecutionDevice.Gpu when _context.SupportsGpu => 2,
            _ => 0
        };
    }

    private void SelectWorkMode(ApplicationMode mode)
    {
        _workMode.IsOn = mode == ApplicationMode.Debug;
    }

    private void UpdateFallbackVisualState()
    {
        var opacity = _biteFallbackEnabled.IsOn ? 1 : 0.45;
        _biteFallbackDelayLabel.Opacity = opacity;
        _biteFallbackValue.Opacity = opacity;
    }

    private static string FormatBiteFallback(double seconds) =>
        UiStrings.Format("BiteFallbackSeconds", seconds);
}
