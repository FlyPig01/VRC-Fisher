using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VrcFisher.Desktop.Contracts;
using VrcFisher.Desktop.Localization;
using VrcFisher.Desktop.Ui;

namespace VrcFisher.Desktop.Pages;

internal sealed class GuidePage : Page
{
    private static readonly Uri SupportedWorld = new(
        "https://vrchat.com/home/world/wrld_ae001ea3-ed05-42f0-adf2-3d47efd10a77/info");

    public GuidePage(IDesktopPageContext context)
    {
        var cover = new Image
        {
            Source = new BitmapImage(new Uri(
                "ms-appx:///Assets/Worlds/wrld_ae001ea3-ed05-42f0-adf2-3d47efd10a77/cover.jpg")),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var worldLink = new Button
        {
            Content = cover,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ToolTipService.SetToolTip(worldLink, UiStrings.Get("OpenSupportedWorld"));
        worldLink.Click += (_, _) => Process.Start(new ProcessStartInfo(SupportedWorld.AbsoluteUri)
        {
            UseShellExecute = true
        });

        var worldCard = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = UiStrings.Get("SupportedWorldTitle"),
                    FontSize = 18,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                worldLink,
                UiFactory.Secondary(UiStrings.Get("SupportedWorldScope"))
            }
        };

        var steps = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                CreateStep("1", UiStrings.Get("GuideStepEnter")),
                CreateStep("2", UiStrings.Get("GuideStepPrepare")),
                CreateStep("3", UiStrings.Format("GuideStepHotkey", context.Options.ToggleHotkey))
            }
        };

        var root = UiFactory.PageStack();
        root.Children.Add(UiFactory.Surface(worldCard));
        root.Children.Add(UiFactory.Section(UiStrings.Get("QuickStart"), steps));
        Content = UiFactory.Scrollable(root);
    }

    private static Grid CreateStep(string number, string text)
    {
        var numberBadge = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"],
            Child = new TextBlock
            {
                Text = number,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            }
        };
        var description = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(numberBadge);
        Grid.SetColumn(description, 1);
        grid.Children.Add(description);
        return grid;
    }
}
