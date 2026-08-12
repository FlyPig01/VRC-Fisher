using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using VrcFisher.Application;
using VrcFisher.Infrastructure.Capture;
using VrcFisher.Infrastructure.Input;
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
        InitializeComponent();
        var root = AppContext.BaseDirectory;
        _layout = new DirectoryLayout(root);
        _layout.Ensure();
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new FileLoggerProvider(Path.Combine(_layout.Logs, "vrc-fisher.log"))));
        _optionsStore = new OptionsStore(root);
        _options = _optionsStore.Load();
        _models = new ModelCatalog(_layout, new HttpClient());
        _capture = new WindowsGraphicsCaptureSource();
        _wgc = new WgcCaptureAdapter(_capture);
        var input = new Win32InputController();
        _detection = new DetectionRuntime(_layout, () => _options, _wgc, _models, input, _loggerFactory.CreateLogger<DetectionRuntime>());
        _runtime = new RuntimeController(_models, _detection, input, _loggerFactory.CreateLogger<RuntimeController>());
        _hotkey = new EmergencyStopHotkey(() => _ = _runtime.StopAsync(CancellationToken.None));
        _hotkey.Start();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(_runtime, _models, _layout, _wgc, _options, SaveOptionsAsync);
        _window.Closed += (_, _) => _ = StopAsync();
        _window.Activate();
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
