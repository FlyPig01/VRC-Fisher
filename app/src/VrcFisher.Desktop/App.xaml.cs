using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using VrcFisher.Application;
using VrcFisher.Desktop.Capture;
using VrcFisher.Desktop.Localization;
using VrcFisher.Desktop.Overlay;
using VrcFisher.Infrastructure.Capture;
using VrcFisher.Infrastructure.Input;
using VrcFisher.Infrastructure.Inference;
using VrcFisher.Infrastructure.Logging;
using VrcFisher.Infrastructure.Models;
using VrcFisher.Infrastructure.Runtime;
using VrcFisher.Core;

namespace VrcFisher.Desktop;

public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly DirectoryLayout _layout;
    private readonly FileLoggerProvider _fileLoggerProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RuntimeController _runtime;
    private readonly DetectionRuntime _detection;
    private readonly WindowsGraphicsCaptureSource _capture;
    private readonly WgcCaptureAdapter _wgc;
    private readonly ModelCatalog _models;
    private readonly ModelDownloadCoordinator _modelDownloads;
    private readonly OptionsStore _optionsStore;
    private readonly Win32InputController _input;
    private readonly Task<HardwareSnapshot> _hardware;
    private AppOptions _options;
    private RuntimeToggleHotkey? _hotkey;
    private readonly SemaphoreSlim _hotkeyGate = new(1, 1);
    private MainWindow? _window;
    private VrChatOverlayController? _overlay;
    private int _overlayFailed;
    private int _stopped;

    public App()
    {
        var installedLayout = DirectoryLayout.FromApplicationBase();
        InitializeComponent();
        _layout = installedLayout;
        _layout.Ensure();
        _optionsStore = new OptionsStore(_layout.Root);
        _options = _optionsStore.Load();
        var optionsPath = Path.Combine(_layout.Config, "user.json");
        if (!File.Exists(optionsPath))
            _options = _options with { Language = ReadInstalledLanguage(_layout.Root) };
        UiStrings.Configure(UiLanguage.Resolve(_options.Language));
        if (_options.Device == VrcFisher.Core.ExecutionDevice.Gpu && !OnnxRuntimeDetector.SupportsDirectML)
            _options = _options with { Device = VrcFisher.Core.ExecutionDevice.Auto };
        _fileLoggerProvider = new FileLoggerProvider(_layout.Logs, _options.WorkMode);
        _loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(_fileLoggerProvider));
        _models = new ModelCatalog(
            _layout,
            new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        _modelDownloads = new ModelDownloadCoordinator(_models);
        _capture = new WindowsGraphicsCaptureSource();
        _wgc = new WgcCaptureAdapter(_capture, _loggerFactory.CreateLogger<WgcCaptureAdapter>());
        var foreground = new VrChatForegroundState();
        _input = new Win32InputController(
            foreground,
            _loggerFactory.CreateLogger<Win32InputController>());
        _hardware = new WindowsHardwareInfoProvider().ReadAsync(CancellationToken.None);
        _detection = new DetectionRuntime(_layout, () => _options, _wgc, _models, _input, _loggerFactory.CreateLogger<DetectionRuntime>());
        _runtime = new RuntimeController(_models, _detection, _input, _loggerFactory.CreateLogger<RuntimeController>());
        _hotkey = TryStartHotkey(_options.ToggleHotkey);
        UnhandledException += OnXamlUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await _models.RefreshAsync(CancellationToken.None);
            await _optionsStore.SaveAsync(_options);
        }
        catch (Exception error)
        {
            _loggerFactory.CreateLogger<App>().LogWarning(error, "startup model or option validation failed");
        }
        _window = new MainWindow(
            _runtime,
            _models,
            _modelDownloads,
            _layout,
            _wgc,
            _options,
            SaveOptionsAsync,
            ChangeHotkeyAsync,
            OnnxRuntimeDetector.SupportsDirectML,
            _hardware);
        _window.Closed += (_, _) => _ = StopAsync();
        try
        {
            _overlay = new VrChatOverlayController(
                _runtime,
                _detection,
                _wgc,
                () => _options,
                HandleOverlayFailure);
        }
        catch (Exception error)
        {
            HandleOverlayFailure(error);
        }
        _window.Activate();
        if (_hotkey is null)
        {
            _window.ShowRuntimeNotice(
                new VrcFisher.Core.RuntimeStatus(VrcFisher.Core.RuntimeMessageCode.HotkeyRegistrationFailed, _options.ToggleHotkey),
                activate: false);
        }
    }

    private static string ReadInstalledLanguage(string root)
    {
        var path = Path.Combine(root, "config", "installer-language.ini");
        var value = File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        return value is not null && UiLanguage.Preferences.Contains(value, StringComparer.Ordinal)
            ? value
            : UiLanguage.English;
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _overlay?.Dispose();
        _overlay = null;
        await _runtime.StopAsync(CancellationToken.None);
        await _modelDownloads.DisposeAsync();
        await _detection.DisposeAsync();
        await _wgc.DisposeAsync();
        _hotkey?.Dispose();
        _loggerFactory.Dispose();
    }

    private async Task ToggleRuntimeAsync()
    {
        if (Volatile.Read(ref _stopped) != 0)
            return;
        if (!await _hotkeyGate.WaitAsync(0))
        {
            if (_runtime.Snapshot.Lifecycle == VrcFisher.Core.RuntimeLifecycle.Starting)
            {
                _input.ReleaseAll();
                await _runtime.StopAsync(CancellationToken.None);
            }
            return;
        }
        try
        {
            if (_runtime.Snapshot.Lifecycle is VrcFisher.Core.RuntimeLifecycle.Starting
                or VrcFisher.Core.RuntimeLifecycle.Running)
            {
                await _runtime.StopAsync(CancellationToken.None);
                return;
            }

            if (Volatile.Read(ref _overlayFailed) != 0 || _overlay is null)
            {
                _window?.ShowRuntimeNotice(
                    new VrcFisher.Core.RuntimeStatus(VrcFisher.Core.RuntimeMessageCode.OverlayUnavailable),
                    activate: true);
                return;
            }

            if (!_wgc.RefreshVrChatTarget())
            {
                _window?.ShowRuntimeNotice(
                    new VrcFisher.Core.RuntimeStatus(VrcFisher.Core.RuntimeMessageCode.VrChatNotRunning),
                    activate: true);
                return;
            }
            if (!_input.IsTargetForeground)
            {
                _window?.ShowRuntimeNotice(
                    new VrcFisher.Core.RuntimeStatus(VrcFisher.Core.RuntimeMessageCode.StartTargetNotForeground),
                    activate: true);
                return;
            }
            await _runtime.StartAsync(CancellationToken.None);
            if (!_runtime.Snapshot.IsObserving)
                _window?.ShowRuntimeNotice(_runtime.Snapshot.Status, activate: false);
        }
        catch (Exception error)
        {
            _loggerFactory.CreateLogger<App>().LogError(error, "F8 runtime toggle failed");
            await _runtime.StopAsync(CancellationToken.None);
            _window?.ShowRuntimeNotice(
                new VrcFisher.Core.RuntimeStatus(VrcFisher.Core.RuntimeMessageCode.UnexpectedFailure, error.Message),
                activate: false);
        }
        finally
        {
            _hotkeyGate.Release();
        }
    }

    private async Task SaveOptionsAsync(AppOptions options)
    {
        options = options.Normalize();
        _options = options;
        _fileLoggerProvider.SetMode(options.WorkMode);
        await _optionsStore.SaveAsync(_options);
    }

    private async Task ChangeHotkeyAsync(string key)
    {
        if (!HotkeyGestureRules.TryNormalize(key, out key))
            throw new ArgumentOutOfRangeException(nameof(key));
        if (string.Equals(key, _options.ToggleHotkey, StringComparison.Ordinal)) return;

        var previousOptions = _options;
        var previousHotkey = _hotkey;
        var replacement = CreateHotkey(key);
        try
        {
            replacement.Start();
            var updatedOptions = previousOptions with { ToggleHotkey = key };
            await _optionsStore.SaveAsync(updatedOptions);
            _options = updatedOptions;
        }
        catch
        {
            replacement.Dispose();
            _options = previousOptions;
            throw;
        }
        _hotkey = replacement;
        previousHotkey?.Dispose();
    }

    private RuntimeToggleHotkey? TryStartHotkey(string key)
    {
        var hotkey = CreateHotkey(key);
        try
        {
            hotkey.Start();
            return hotkey;
        }
        catch (Exception error)
        {
            hotkey.Dispose();
            _loggerFactory.CreateLogger<App>().LogWarning(error, "failed to register {Hotkey}", key);
            return null;
        }
    }

    private RuntimeToggleHotkey CreateHotkey(string key) => new(
        key,
        () => _ = ToggleRuntimeAsync(),
        () => _input.IsTargetForeground,
        () =>
        {
            _input.ReleaseAll();
            _ = StopAfterTargetLossAsync();
        });

    private async Task StopAfterTargetLossAsync()
    {
        if (_runtime.Snapshot.Lifecycle is not (VrcFisher.Core.RuntimeLifecycle.Starting
            or VrcFisher.Core.RuntimeLifecycle.Running))
            return;
        await _runtime.StopAsync(CancellationToken.None);
        _window?.ShowRuntimeNotice(
            new VrcFisher.Core.RuntimeStatus(VrcFisher.Core.RuntimeMessageCode.TargetNotForeground),
            activate: false);
    }

    private void HandleOverlayFailure(Exception error)
    {
        if (Interlocked.Exchange(ref _overlayFailed, 1) != 0) return;
        _loggerFactory.CreateLogger<App>().LogError(error, "VRChat overlay failed");
        _ = StopAfterOverlayFailureAsync();
    }

    private async Task StopAfterOverlayFailureAsync()
    {
        if (_runtime.Snapshot.IsObserving)
            await _runtime.StopAsync(CancellationToken.None);
        _window?.ShowRuntimeNotice(
            new VrcFisher.Core.RuntimeStatus(VrcFisher.Core.RuntimeMessageCode.OverlayUnavailable),
            activate: true);
    }

    private void OnXamlUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        _input.ReleaseAll();
        _loggerFactory.CreateLogger<App>().LogCritical(args.Exception, "unhandled XAML exception; input released");
    }

    private void OnDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs args)
    {
        _input.ReleaseAll();
        _loggerFactory.CreateLogger<App>().LogCritical(
            args.ExceptionObject as Exception,
            "unhandled process exception; input released; terminating={Terminating}",
            args.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        _input.ReleaseAll();
        _loggerFactory.CreateLogger<App>().LogError(args.Exception, "unobserved task exception; input released");
        args.SetObserved();
    }
}
