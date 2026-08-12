using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcFisher.Application;

namespace VrcFisher.Desktop;

public sealed class RunPage : Page
{
    private TextBlock _status = null!;
    private TextBlock _provider = null!;

    public RunPage()
    {
        var root = new StackPanel { Spacing = 16 };
        root.Children.Add(new TextBlock { Text = "运行", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _status = new TextBlock { Text = "正在加载状态...", TextWrapping = TextWrapping.Wrap };
        _provider = new TextBlock { Text = "Provider: Unavailable" };
        root.Children.Add(_status);
        root.Children.Add(_provider);
        var observe = new Button { Content = "仅观察", HorizontalAlignment = HorizontalAlignment.Left };
        var automatic = new Button { Content = "自动运行", HorizontalAlignment = HorizontalAlignment.Left };
        var stop = new Button { Content = "停止并释放鼠标", HorizontalAlignment = HorizontalAlignment.Left };
        observe.Click += async (_, _) => await StartAsync(false);
        automatic.Click += async (_, _) => await StartAsync(true);
        stop.Click += async (_, _) => await StopAsync();
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { observe, automatic, stop } });
        Content = root;
        Loaded += (_, _) => Refresh();
    }

    private MainWindow Window => (MainWindow)Tag!;

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
        _status.Text = $"阶段：{snapshot.Phase}\n状态：{snapshot.Message}";
        _provider.Text = $"Provider：{snapshot.Provider}\n模型：{(snapshot.ModelsReady ? "已就绪" : "未安装或未通过校验")}";
    }
}

public sealed class ModelsPage : Page
{
    private readonly StackPanel _list = new() { Spacing = 8 };
    private MainWindow _window = null!;

    public ModelsPage()
    {
        var root = new StackPanel { Spacing = 16 };
        root.Children.Add(new TextBlock { Text = "模型", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = "模型必须通过完整性校验；当前没有正式模型时，运行按钮保持禁用。", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(_list);
        var refresh = new Button { Content = "刷新状态" };
        var delete = new Button { Content = "删除模型" };
        refresh.Click += async (_, _) => await RefreshAsync();
        delete.Click += async (_, _) => { if (_window.Models is VrcFisher.Infrastructure.Models.ModelCatalog catalog) await catalog.DeleteModelsAsync(CancellationToken.None); await RefreshAsync(); };
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { refresh, delete } });
        Content = root;
        Loaded += async (_, _) => { _window = (MainWindow)Tag!; await RefreshAsync(); };
    }

    private async Task RefreshAsync()
    {
        await _window.Models.RefreshAsync(CancellationToken.None);
        _list.Children.Clear();
        foreach (var item in _window.Models.GetStatus())
            _list.Children.Add(new TextBlock { Text = $"{item.Name}: {(item.Installed ? item.Message : "未安装")}" });
    }
}

public sealed class SettingsPage : Page
{
    public SettingsPage()
    {
        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "设置", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                new TextBlock { Text = "显示器、设备和阈值配置将在捕获适配器完成后启用。当前软件根目录：" },
                new TextBlock { Text = AppContext.BaseDirectory, TextWrapping = TextWrapping.Wrap }
            }
        };
    }
}

public sealed class DiagnosticsPage : Page
{
    public DiagnosticsPage()
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Loaded += (_, _) =>
        {
            if (Tag is MainWindow window)
            {
                var snapshot = window.Runtime.Snapshot;
                text.Text = $"Provider：{snapshot.Provider}\n捕获帧：{snapshot.FramesCaptured}\n丢弃帧：{snapshot.FramesDropped}\n状态：{snapshot.Message}";
            }
        };
        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "诊断", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                text,
                new TextBlock { Text = "诊断预览默认关闭。没有正式模型和显示器捕获时，不显示伪造的识别结果。", TextWrapping = TextWrapping.Wrap }
            }
        };
    }
}
