using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VrcFisher.Desktop.Ui;

internal static class UiFactory
{
    public static StackPanel PageStack() => new()
    {
        Spacing = 24,
        MaxWidth = 960,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    public static ScrollViewer Scrollable(UIElement content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    public static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        Style = Resource<Style>("PageTitleTextStyle")
    };

    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        Style = Resource<Style>("SectionTitleTextStyle")
    };

    public static TextBlock Secondary(string text = "") => new()
    {
        Text = text,
        Style = Resource<Style>("SecondaryTextStyle")
    };

    public static Border Surface(UIElement content) => new()
    {
        Child = content,
        Style = Resource<Style>("SurfaceBorderStyle")
    };

    public static StackPanel Section(string title, UIElement content) => new()
    {
        Spacing = 10,
        Children =
        {
            SectionTitle(title),
            Surface(content)
        }
    };

    public static Button CommandButton(Symbol symbol, string text, bool accent = false)
    {
        var button = new Button
        {
            Content = IconLabel(symbol, text),
            MinHeight = 40,
            Padding = new Thickness(14, 8, 14, 8)
        };
        if (accent) button.Style = Resource<Style>("AccentButtonStyle");
        return button;
    }

    public static StackPanel StatusCell(string label, TextBlock value) => new()
    {
        Spacing = 6,
        Children =
        {
            Secondary(label),
            value
        }
    };

    public static Grid FormRow(string label, FrameworkElement control, string? description = null)
    {
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(labelText);

        var controlStack = new StackPanel
        {
            Spacing = 6,
            MinHeight = 40,
            VerticalAlignment = VerticalAlignment.Center
        };
        controlStack.Children.Add(control);
        if (!string.IsNullOrWhiteSpace(description))
            controlStack.Children.Add(Secondary(description));
        Grid.SetColumn(controlStack, 1);
        grid.Children.Add(controlStack);
        return grid;
    }

    private static StackPanel IconLabel(Symbol symbol, string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new SymbolIcon(symbol),
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
        }
    };

    private static T Resource<T>(string key) where T : class =>
        (T)Microsoft.UI.Xaml.Application.Current.Resources[key];
}
