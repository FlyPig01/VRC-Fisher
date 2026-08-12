using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using VrcFisher.Application;
using VrcFisher.Infrastructure.Capture;
using VrcFisher.Infrastructure.Input;
using VrcFisher.Infrastructure.Inference;
using VrcFisher.Infrastructure.Logging;
using VrcFisher.Infrastructure.Models;
using VrcFisher.Infrastructure.Runtime;

namespace VrcFisher.Desktop;

public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly DirectoryLayout _layout;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RuntimeController _runtime;
    private readonly DetectionRuntime _detection;
    private readonly WindowsGraphicsCaptureSource _capture;
    private readonly WgcCaptureAdapter _wgc;
    private readonly ModelCatalog _models;
    private readonly OptionsStore _optionsStore;
    private AppOptions _options;
    private readonly EmergencyStopHotkey _hotkey;
    private MainWindow? _window;
    private int _stopped;

    public App()
    {
        var installedLayout = DirectoryLayout.FromApplicationBase();
        ApplyInstalledLanguage(installedLayout.Root);
        InitializeComponent();
        _layout = installedLayout;
        _layout.Ensure();
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new FileLoggerProvider(Path.Combine(_layout.Logs, "vrc-fisher.log"))));
        _optionsStore = new OptionsStore(_layout.Root);
        _options = _optionsStore.Load();
        if (_options.Device == VrcFisher.Core.ExecutionDevice.Gpu && !OnnxRuntimeDetector.SupportsDirectML)
            _options = _options with { Device = VrcFisher.Core.ExecutionDevice.Auto };
        _models = new ModelCatalog(
            _layout,
            new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        _capture = new WindowsGraphicsCaptureSource();
        _wgc = new WgcCaptureAdapter(_capture);
        var input = new Win32InputController();
        _detection = new DetectionRuntime(_layout, () => _options, _wgc, _models, input, _loggerFactory.CreateLogger<DetectionRuntime>());
        _runtime = new RuntimeController(_models, _detection, input, _loggerFactory.CreateLogger<RuntimeController>());
        _hotkey = new EmergencyStopHotkey(() => _ = _runtime.StopAsync(CancellationToken.None));
        _hotkey.Start();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (args.Arguments.Contains("--download-models", StringComparison.OrdinalIgnoreCase))
        {
            _ = RunModelDownloadCommandAsync();
            return;
        }
        try
        {
            await _models.RefreshAsync(CancellationToken.None);
            await _optionsStore.SaveAsync(_options);
        }
        catch (Exception error)
        {
            _loggerFactory.CreateLogger<App>().LogWarning(error, "startup model or option validation failed");
        }
        _window = new MainWindow(_runtime, _models, _layout, _wgc, _options, SaveOptionsAsync);
        _window.Closed += (_, _) => _ = StopAsync();
        _window.Activate();
    }

    private async Task RunModelDownloadCommandAsync()
    {
        try
        {
            await _models.DownloadLatestAsync(progress: null, CancellationToken.None);
            Environment.ExitCode = 0;
        }
        catch (Exception error)
        {
            _loggerFactory.CreateLogger<App>().LogError(error, "model download command failed");
            Environment.ExitCode = 1;
        }
        finally
        {
            await StopAsync();
            Environment.Exit(Environment.ExitCode);
        }
    }

    private static void ApplyInstalledLanguage(string root)
    {
        var path = Path.Combine(root, "config", "installer-language.ini");
        var value = File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        UiStrings.Configure(value is "zh-CN" or "en-US" ? value : "en-US");
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        await _runtime.StopAsync(CancellationToken.None);
        await _detection.DisposeAsync();
        await _wgc.DisposeAsync();
        _hotkey.Dispose();
        _loggerFactory.Dispose();
    }

    private async Task SaveOptionsAsync(AppOptions options)
    {
        _options = options;
        await _optionsStore.SaveAsync(options);
    }
}
