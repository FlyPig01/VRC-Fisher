using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Desktop.Contracts;
using VrcFisher.Desktop.Localization;
using VrcFisher.Desktop.Ui;

namespace VrcFisher.Desktop.Pages;

internal sealed class ModelsPage : Page
{
    private readonly IDesktopPageContext _context;
    private readonly TextBlock _locatorStatus = ModelStatusText();
    private readonly TextBlock _minigameStatus = ModelStatusText();
    private readonly TextBlock _totalSize = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _message = UiFactory.Secondary();
    private readonly ProgressBar _progress = new()
    {
        Minimum = 0,
        Maximum = 1,
        Visibility = Visibility.Collapsed
    };
    private readonly Button _action = new()
    {
        MinWidth = 110,
        MinHeight = 40,
        Padding = new Thickness(14, 8, 14, 8)
    };
    private readonly Button _cancel;
    private readonly CancellationTokenSource _pageCancellation = new();
    private ModelAction _actionKind;

    public ModelsPage(IDesktopPageContext context)
    {
        _context = context;
        _cancel = UiFactory.CommandButton(Symbol.Cancel, UiStrings.Get("CancelDownload"));
        _cancel.Visibility = Visibility.Collapsed;
        _action.Click += async (_, _) => await ExecuteActionAsync();
        _cancel.Click += (_, _) => _context.ModelDownloads.Cancel();

        var modelDetails = new StackPanel
        {
            Spacing = 22,
            Children =
            {
                CreateModelRow(
                    "locator.onnx",
                    UiStrings.Get("LocatorModelDescription"),
                    UiStrings.Get("LocatorModelTooltip"),
                    _locatorStatus),
                CreateModelRow(
                    "minigame.onnx",
                    UiStrings.Get("MinigameModelDescription"),
                    UiStrings.Get("MinigameModelTooltip"),
                    _minigameStatus)
            }
        };

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _action, _cancel }
        };
        var modelPackage = new Grid();
        modelPackage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modelPackage.Children.Add(modelDetails);

        _message.VerticalAlignment = VerticalAlignment.Center;
        var statusRow = new Grid { ColumnSpacing = 16 };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusRow.Children.Add(_message);
        Grid.SetColumn(actionPanel, 1);
        statusRow.Children.Add(actionPanel);

        var modelsFolder = Path.Combine(_context.SoftwareRoot, "models");
        var pathText = new TextBlock
        {
            Text = modelsFolder,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(pathText, modelsFolder);
        var openFolder = UiFactory.CommandButton(Symbol.OpenFile, UiStrings.Get("OpenFolder"));
        openFolder.Click += (_, _) => _context.OpenModelsFolder();

        var storage = new Grid { ColumnSpacing = 16, RowSpacing = 14 };
        storage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        storage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        storage.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        storage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        storage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        storage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddStorageRow(storage, 0, UiStrings.Get("ModelsFolder"), pathText, openFolder);

        var source = new HyperlinkButton
        {
            Content = UiStrings.Format("ModelSourceName", _context.Models.Repository),
            NavigateUri = _context.Models.SourceUri,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        AddStorageRow(storage, 1, UiStrings.Get("TotalModelSize"), _totalSize, new Border());
        AddStorageRow(storage, 2, UiStrings.Get("ModelSource"), source, new Border());

        var root = UiFactory.PageStack();
        root.Children.Add(UiFactory.PageTitle(UiStrings.Get("Models")));
        root.Children.Add(UiFactory.Secondary(UiStrings.Get("ModelsDescription")));
        root.Children.Add(UiFactory.Surface(modelPackage));
        root.Children.Add(_progress);
        root.Children.Add(statusRow);
        root.Children.Add(UiFactory.Surface(storage));
        Content = UiFactory.Scrollable(root);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        _context.ModelDownloads.StateChanged += OnDownloadStateChanged;
        ApplyDownloadState(_context.ModelDownloads.Snapshot);
        await RefreshAsync(checkForUpdates: true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _context.ModelDownloads.StateChanged -= OnDownloadStateChanged;
        _pageCancellation.Cancel();
    }

    private void OnDownloadStateChanged(object? sender, ModelDownloadSnapshot snapshot) =>
        DispatcherQueue.TryEnqueue(() => ApplyDownloadState(snapshot));

    private async Task RefreshAsync(bool checkForUpdates)
    {
        try
        {
            await _context.Models.RefreshAsync(_pageCancellation.Token);
            ApplyCatalogState();
            if (!checkForUpdates || _context.ModelDownloads.Snapshot.IsActive)
            {
                ApplyDownloadState(_context.ModelDownloads.Snapshot);
                return;
            }

            _action.IsEnabled = false;
            _message.Text = UiStrings.Get("CheckingUpdates");
            try
            {
                await _context.Models.CheckForUpdatesAsync(_pageCancellation.Token);
                _message.Text = string.Empty;
            }
            catch (OperationCanceledException) when (_pageCancellation.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                _message.Text = UiStrings.Get(
                    _context.Models.IsReady
                        ? "UpdateCheckFailed"
                        : "UpdateCheckFailedNoModels");
            }
            ApplyCatalogState();
        }
        catch (OperationCanceledException) when (_pageCancellation.IsCancellationRequested)
        {
        }
    }

    private void ApplyCatalogState()
    {
        var statuses = _context.Models.GetStatus();
        SetStatus(_locatorStatus, statuses.FirstOrDefault(item => item.Name == "locator.onnx"));
        SetStatus(_minigameStatus, statuses.FirstOrDefault(item => item.Name == "minigame.onnx"));
        _totalSize.Text = FormatSize(_context.Models.InstalledSize);

        var needsDownload = statuses.Count < 2
            || statuses.Any(item => !item.Installed || !item.Valid);
        SetAction(needsDownload
            ? ModelAction.Download
            : _context.Models.UpdateAvailable
                ? ModelAction.Update
                : ModelAction.Delete);
    }

    private void SetAction(ModelAction action)
    {
        _actionKind = action;
        _action.ClearValue(FrameworkElement.StyleProperty);
        _action.ClearValue(Control.BackgroundProperty);
        _action.ClearValue(Control.ForegroundProperty);
        switch (action)
        {
            case ModelAction.Download:
                _action.Content = ActionContent(Symbol.Download, UiStrings.Get("DownloadModels"));
                _action.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
                break;
            case ModelAction.Update:
                _action.Content = ActionContent(Symbol.Sync, UiStrings.Get("UpdateModels"));
                _action.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 65));
                _action.Foreground = new SolidColorBrush(Colors.White);
                break;
            default:
                _action.Content = ActionContent(Symbol.Delete, UiStrings.Get("DeleteModels"));
                break;
        }
        _action.IsEnabled = !_context.ModelDownloads.Snapshot.IsActive;
    }

    private async Task ExecuteActionAsync()
    {
        var action = _actionKind;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiStrings.Get(action switch
            {
                ModelAction.Download => "ConfirmDownloadTitle",
                ModelAction.Update => "ConfirmUpdateTitle",
                _ => "ConfirmDeleteTitle"
            }),
            Content = UiStrings.Get(action switch
            {
                ModelAction.Download => "ConfirmDownloadModels",
                ModelAction.Update => "ConfirmUpdateModels",
                _ => "ConfirmDeleteModels"
            }),
            PrimaryButtonText = UiStrings.Get(action switch
            {
                ModelAction.Download => "DownloadModels",
                ModelAction.Update => "UpdateModels",
                _ => "DeleteModels"
            }),
            CloseButtonText = UiStrings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (action == ModelAction.Delete)
            await DeleteAsync();
        else
            _context.ModelDownloads.Start();
    }

    private void ApplyDownloadState(ModelDownloadSnapshot snapshot)
    {
        SetDownloading(snapshot.IsActive);
        switch (snapshot.Phase)
        {
            case ModelDownloadPhase.Resolving:
                _message.Text = UiStrings.Get("CheckingModels");
                break;
            case ModelDownloadPhase.Downloading when snapshot.Progress is not null:
                var value = snapshot.Progress;
                _progress.Value = value.BytesTotal <= 0
                    ? 0
                    : (double)value.BytesDownloaded / value.BytesTotal;
                var speed = snapshot.BytesPerSecond > 0
                    ? $" · {DataSizeFormatter.Format((long)snapshot.BytesPerSecond)}/s"
                    : string.Empty;
                _message.Text = $"{value.CurrentFile}: {DataSizeFormatter.FormatProgress(value.BytesDownloaded, value.BytesTotal)}{speed}";
                break;
            case ModelDownloadPhase.Completed:
                _message.Text = UiStrings.Get("DownloadComplete");
                ApplyCatalogState();
                break;
            case ModelDownloadPhase.Cancelled:
                _message.Text = UiStrings.Get("DownloadCancelled");
                ApplyCatalogState();
                break;
            case ModelDownloadPhase.Failed:
                _message.Text = UiStrings.Format(
                    "DownloadFailed",
                    snapshot.Error ?? UiStrings.Get("UnknownError"));
                ApplyCatalogState();
                break;
        }
    }

    private async Task DeleteAsync()
    {
        await _context.Models.DeleteModelsAsync(_pageCancellation.Token);
        _message.Text = UiStrings.Get("ModelsDeleted");
        await RefreshAsync(checkForUpdates: false);
    }

    private void SetDownloading(bool isDownloading)
    {
        _progress.Visibility = isDownloading ? Visibility.Visible : Visibility.Collapsed;
        _cancel.Visibility = isDownloading ? Visibility.Visible : Visibility.Collapsed;
        _action.IsEnabled = !isDownloading;
    }

    private static Grid CreateModelRow(
        string name,
        string purpose,
        string tooltip,
        TextBlock status)
    {
        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labels = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = name,
                    FontSize = 17,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                UiFactory.Secondary(purpose)
            }
        };
        grid.Children.Add(labels);
        Grid.SetColumn(status, 1);
        grid.Children.Add(status);
        ToolTipService.SetToolTip(grid, tooltip);
        return grid;
    }

    private static StackPanel ActionContent(Symbol symbol, string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new SymbolIcon(symbol),
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
        }
    };

    private static void AddStorageRow(Grid grid, int row, string label, FrameworkElement value, FrameworkElement action)
    {
        var labelText = UiFactory.Secondary(label);
        labelText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(labelText, row);
        grid.Children.Add(labelText);
        Grid.SetColumn(value, 1);
        Grid.SetRow(value, row);
        grid.Children.Add(value);
        Grid.SetColumn(action, 2);
        Grid.SetRow(action, row);
        grid.Children.Add(action);
    }

    private static TextBlock ModelStatusText() => new()
    {
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static void SetStatus(TextBlock target, ModelStatus? status)
    {
        target.Text = status is null || !status.Installed
            ? UiStrings.Get("ModelStatusMissing")
            : status.Valid
                ? UiStrings.Format("ModelRowReady", FormatSize(status.Size))
                : UiStrings.Format("ModelRowInvalid", FormatSize(status.Size));
    }

    private static string FormatSize(long bytes)
        => DataSizeFormatter.Format(bytes);

    private enum ModelAction
    {
        Download,
        Update,
        Delete
    }
}
