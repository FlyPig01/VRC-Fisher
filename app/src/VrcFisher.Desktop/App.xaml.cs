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
    private readonly Win32InputController _input;
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
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new FileLoggerProvider(Path.Combine(_layout.Logs, "vrc-fisher.log"))));
        _optionsStore = new OptionsStore(_layout.Root);
        _options = _optionsStore.Load();
        var optionsPath = Path.Combine(_layout.Config, "user.json");
        if (!File.Exists(optionsPath))
            _options = _options with { Language = ReadInstalledLanguage(_layout.Root) };
        UiStrings.Configure(UiLanguage.Resolve(_options.Language));
        if (_options.Device == VrcFisher.Core.ExecutionDevice.Gpu && !OnnxRuntimeDetector.SupportsDirectML)
            _options = _options with { Device = VrcFisher.Core.ExecutionDevice.Auto };
        _models = new ModelCatalog(
            _layout,
            new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        _capture = new WindowsGraphicsCaptureSource();
        _wgc = new WgcCaptureAdapter(_capture);
        _input = new Win32InputController();
        _detection = new DetectionRuntime(_layout, () => _options, _wgc, _models, _input, _loggerFactory.CreateLogger<DetectionRuntime>());
        _runtime = new RuntimeController(_models, _detection, _input, _loggerFactory.CreateLogger<RuntimeController>());
        _hotkey = TryStartHotkey(_options.ToggleHotkey);
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
        _window = new MainWindow(
            _runtime,
            _models,
            _layout,
            _wgc,
            _options,
            SaveOptionsAsync,
            ChangeHotkeyAsync,
            OnnxRuntimeDetector.SupportsDirectML);
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
        await _detection.DisposeAsync();
        await _wgc.DisposeAsync();
        _hotkey?.Dispose();
        _loggerFactory.Dispose();
    }

    private async Task ToggleRuntimeAsync()
    {
        if (Volatile.Read(ref _stopped) != 0 || !await _hotkeyGate.WaitAsync(0))
            return;
        try
        {
            if (_runtime.Snapshot.IsObserving)
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
                _window?.ShowRuntimeNotice(_runtime.Snapshot.Status, activate: true);
        }
        catch (Exception error)
        {
            _loggerFactory.CreateLogger<App>().LogError(error, "F8 runtime toggle failed");
            await _runtime.StopAsync(CancellationToken.None);
            _window?.ShowRuntimeNotice(
                new VrcFisher.Core.RuntimeStatus(VrcFisher.Core.RuntimeMessageCode.UnexpectedFailure, error.Message),
                activate: true);
        }
        finally
        {
            _hotkeyGate.Release();
        }
    }

    private async Task SaveOptionsAsync(AppOptions options)
    {
        _options = options.Normalize();
        await _optionsStore.SaveAsync(_options);
    }

    private async Task ChangeHotkeyAsync(string key)
    {
        if (!AppOptions.SupportedToggleHotkeys.Contains(key, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(key));
        if (string.Equals(key, _options.ToggleHotkey, StringComparison.Ordinal)) return;

        var previousOptions = _options;
        var previousHotkey = _hotkey;
        var replacement = new RuntimeToggleHotkey(key, () => _ = ToggleRuntimeAsync());
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
        var hotkey = new RuntimeToggleHotkey(key, () => _ = ToggleRuntimeAsync());
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
}
