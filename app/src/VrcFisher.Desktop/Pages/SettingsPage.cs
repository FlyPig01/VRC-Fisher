using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Desktop.Contracts;
using VrcFisher.Desktop.Localization;
using VrcFisher.Desktop.Ui;

namespace VrcFisher.Desktop.Pages;

internal sealed class SettingsPage : Page
{
    private readonly IDesktopPageContext _context;
    private readonly ComboBox _language = new() { MinWidth = 220 };
    private readonly ComboBox _workMode = new() { MinWidth = 220 };
    private readonly ComboBox _device = new() { MinWidth = 220 };
    private readonly ComboBox _hotkey = new() { MinWidth = 220 };
    private readonly ToggleSwitch _biteFallbackEnabled = new();
    private readonly Slider _biteFallback = new()
    {
        Minimum = 5,
        Maximum = 30,
        StepFrequency = 1,
        Width = 320,
        HorizontalAlignment = HorizontalAlignment.Left
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

        AddWorkMode(ApplicationMode.Run, new SymbolIcon(Symbol.Play), "RunMode", "RunModeDescription");
        AddWorkMode(ApplicationMode.Debug, new FontIcon { Glyph = "\uEBE8" }, "DebugMode", "DebugModeDescription");
        _workMode.SelectionChanged += async (_, _) => await SaveWorkModeAsync();

        AddDevice(ExecutionDevice.Auto, "DeviceAuto");
        AddDevice(ExecutionDevice.Cpu, "DeviceCpu");
        if (_context.SupportsGpu) AddDevice(ExecutionDevice.Gpu, "DeviceGpu");
        _device.SelectionChanged += async (_, _) => await SaveDeviceAsync();

        foreach (var hotkey in AppOptions.SupportedToggleHotkeys)
            _hotkey.Items.Add(new ComboBoxItem { Content = hotkey, Tag = hotkey });
        _hotkey.SelectionChanged += async (_, _) => await SaveHotkeyAsync();

        _biteFallbackEnabled.OnContent = UiStrings.Get("Enabled");
        _biteFallbackEnabled.OffContent = UiStrings.Get("Disabled");
        _biteFallbackEnabled.Toggled += async (_, _) =>
        {
            _biteFallback.IsEnabled = _biteFallbackEnabled.IsOn;
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
        var hotkeyRow = UiFactory.FormRow(UiStrings.Get("ToggleHotkey"), _hotkey);
        ToolTipService.SetToolTip(hotkeyRow, UiStrings.Get("ToggleHotkeyDescription"));
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

        var fallbackControl = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children = { _biteFallback, _biteFallbackValue }
        };
        var fallbackDelayRow = UiFactory.FormRow(
            UiStrings.Get("BiteFallbackDelay"),
            fallbackControl);
        ToolTipService.SetToolTip(fallbackDelayRow, UiStrings.Get("BiteFallbackDelayDescription"));
        var fallbackToggleRow = UiFactory.FormRow(UiStrings.Get("BiteFallback"), _biteFallbackEnabled);
        ToolTipService.SetToolTip(fallbackToggleRow, UiStrings.Get("BiteFallbackDescription"));

        var automationSettings = new StackPanel
        {
            Spacing = 20,
            Children =
            {
                fallbackToggleRow,
                fallbackDelayRow
            }
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
        root.Children.Add(UiFactory.PageTitle(UiStrings.Get("Settings")));
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
        SelectHotkey(_context.Options.ToggleHotkey);
        _biteFallbackEnabled.IsOn = _context.Options.BiteFallbackEnabled;
        _biteFallback.Value = _context.Options.BiteFallbackSeconds;
        _biteFallback.IsEnabled = _context.Options.BiteFallbackEnabled;
        _biteFallbackValue.Text = FormatBiteFallback(_biteFallback.Value);
        _refreshing = false;
    }

    private async Task SaveLanguageAsync()
    {
        if (_refreshing
            || _language.SelectedItem is not ComboBoxItem item
            || item.Tag is not string language)
        {
            return;
        }
        await _context.ChangeLanguageAsync(language);
    }

    private async Task SaveDeviceAsync()
    {
        if (_refreshing
            || _device.SelectedItem is not ComboBoxItem item
            || item.Tag is not ExecutionDevice device)
        {
            return;
        }
        await _context.ChangeDeviceAsync(device);
    }

    private async Task SaveWorkModeAsync()
    {
        if (_refreshing
            || _workMode.SelectedItem is not ComboBoxItem item
            || item.Tag is not ApplicationMode mode)
        {
            return;
        }
        await _context.SaveOptionsAsync(_context.Options with { WorkMode = mode });
    }

    private async Task SaveHotkeyAsync()
    {
        if (_refreshing
            || _confirmingHotkey
            || _hotkey.SelectedItem is not ComboBoxItem item
            || item.Tag is not string hotkey
            || string.Equals(hotkey, _context.Options.ToggleHotkey, StringComparison.Ordinal))
        {
            return;
        }

        _confirmingHotkey = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = UiStrings.Get("ConfirmHotkeyTitle"),
                Content = UiStrings.Format(
                    "ConfirmHotkeyMessage",
                    _context.Options.ToggleHotkey,
                    hotkey),
                PrimaryButtonText = UiStrings.Get("ConfirmChange"),
                CloseButtonText = UiStrings.Get("Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                SelectHotkey(_context.Options.ToggleHotkey);
                return;
            }

            try
            {
                await _context.ChangeHotkeyAsync(hotkey);
            }
            catch (Exception error)
            {
                SelectHotkey(_context.Options.ToggleHotkey);
                var failure = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = UiStrings.Get("HotkeyChangeFailedTitle"),
                    Content = UiStrings.Format("HotkeyChangeFailed", hotkey, error.Message),
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

    private void AddDevice(ExecutionDevice device, string resourceKey) =>
        _device.Items.Add(new ComboBoxItem { Content = UiStrings.Get(resourceKey), Tag = device });

    private void AddWorkMode(
        ApplicationMode mode,
        IconElement icon,
        string labelKey,
        string descriptionKey)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                icon,
                new TextBlock
                {
                    Text = UiStrings.Get(labelKey),
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        var item = new ComboBoxItem { Content = content, Tag = mode };
        ToolTipService.SetToolTip(item, UiStrings.Get(descriptionKey));
        _workMode.Items.Add(item);
    }

    private void SelectLanguage(string language)
    {
        foreach (var item in _language.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, language, StringComparison.Ordinal))
            {
                _language.SelectedItem = item;
                return;
            }
        }
        _language.SelectedIndex = 0;
    }

    private void SelectDevice(ExecutionDevice device)
    {
        foreach (var item in _device.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is ExecutionDevice candidate && candidate == device)
            {
                _device.SelectedItem = item;
                return;
            }
        }
        _device.SelectedIndex = 0;
    }

    private void SelectWorkMode(ApplicationMode mode)
    {
        foreach (var item in _workMode.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is ApplicationMode candidate && candidate == mode)
            {
                _workMode.SelectedItem = item;
                return;
            }
        }
        _workMode.SelectedIndex = 0;
    }

    private void SelectHotkey(string hotkey)
    {
        var wasRefreshing = _refreshing;
        _refreshing = true;
        foreach (var item in _hotkey.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, hotkey, StringComparison.Ordinal))
            {
                _hotkey.SelectedItem = item;
                _refreshing = wasRefreshing;
                return;
            }
        }
        _hotkey.SelectedIndex = 2;
        _refreshing = wasRefreshing;
    }

    private static string FormatBiteFallback(double seconds) =>
        UiStrings.Format("BiteFallbackSeconds", seconds);
}
