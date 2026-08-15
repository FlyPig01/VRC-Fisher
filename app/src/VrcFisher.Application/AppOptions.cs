using System.Text.Json;
using VrcFisher.Core;

namespace VrcFisher.Application;

public sealed record AppOptions(
    string Language = UiLanguage.English,
    ExecutionDevice Device = ExecutionDevice.Auto,
    ApplicationMode WorkMode = ApplicationMode.Run,
    double ConfidenceThreshold = 0.35,
    double IoUThreshold = 0.45,
    bool BiteFallbackEnabled = false,
    double BiteFallbackSeconds = 15,
    string ToggleHotkey = "F8",
    int SettingsVersion = 3)
{
    public const int CurrentSettingsVersion = 3;
    public static readonly IReadOnlyList<string> SupportedToggleHotkeys =
        ["F6", "F7", "F8", "F9", "F10", "F11", "F12"];

    public static AppOptions Default => new();

    public AppOptions Normalize()
    {
        var confidence = double.IsFinite(ConfidenceThreshold)
            ? Math.Clamp(ConfidenceThreshold, 0.01, 0.99)
            : Default.ConfidenceThreshold;
        var iou = double.IsFinite(IoUThreshold)
            ? Math.Clamp(IoUThreshold, 0.01, 0.99)
            : Default.IoUThreshold;
        var fallback = double.IsFinite(BiteFallbackSeconds) && BiteFallbackSeconds >= 5
            ? Math.Clamp(BiteFallbackSeconds, 5, 30)
            : Default.BiteFallbackSeconds;
        return this with
        {
            Language = UiLanguage.Preferences.Contains(Language, StringComparer.Ordinal)
                ? Language
                : Default.Language,
            WorkMode = Enum.IsDefined(WorkMode) ? WorkMode : Default.WorkMode,
            ConfidenceThreshold = confidence,
            IoUThreshold = iou,
            BiteFallbackSeconds = fallback,
            ToggleHotkey = SupportedToggleHotkeys.Contains(ToggleHotkey, StringComparer.Ordinal)
                ? ToggleHotkey
                : Default.ToggleHotkey,
            SettingsVersion = CurrentSettingsVersion
        };
    }
}

public sealed class OptionsStore(string rootDirectory)
{
    private readonly string _path = Path.Combine(rootDirectory, "config", "user.json");

    public AppOptions Load()
    {
        if (!File.Exists(_path)) return AppOptions.Default;
        try
        {
            var json = File.ReadAllText(_path);
            var value = JsonSerializer.Deserialize<AppOptions>(json, JsonOptions.Default)
                ?? AppOptions.Default;
            using var document = JsonDocument.Parse(json);
            var isLegacy = !document.RootElement.TryGetProperty("settingsVersion", out var version)
                || !version.TryGetInt32(out var settingsVersion)
                || settingsVersion < AppOptions.CurrentSettingsVersion;
            if (isLegacy && value.BiteFallbackSeconds <= 5)
                value = value with { BiteFallbackSeconds = AppOptions.Default.BiteFallbackSeconds };
            return value.Normalize();
        }
        catch (JsonException)
        {
            return AppOptions.Default;
        }
    }

    public async Task SaveAsync(AppOptions options, CancellationToken cancellationToken = default)
    {
        options = options.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(
            _path,
            JsonSerializer.Serialize(options, JsonOptions.Default),
            cancellationToken);
    }
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}

public sealed class DirectoryLayout(string rootDirectory)
{
    public string Root { get; } = Path.GetFullPath(rootDirectory);
    public string Config => Path.Combine(Root, "config");
    public string Models => Path.Combine(Root, "models");
    public string Downloads => Path.Combine(Root, "downloads");
    public string Logs => Path.Combine(Root, "logs");

    public void Ensure()
    {
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(Models);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(Logs);
    }

    public static DirectoryLayout FromApplicationBase()
    {
        var binaryRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var parent = Directory.GetParent(binaryRoot.TrimEnd(Path.DirectorySeparatorChar));
        var installedRoot = parent?.FullName;
        return installedRoot is not null
            && File.Exists(Path.Combine(installedRoot, "release.json"))
            ? new DirectoryLayout(installedRoot)
            : new DirectoryLayout(binaryRoot);
    }
}

public interface IModelCatalog
{
    IReadOnlyList<ModelStatus> GetStatus();
    bool IsReady { get; }
    bool AutomaticAllowed { get; }
    long InstalledSize { get; }
    string Repository { get; }
    string? InstalledVersion { get; }
    string? LatestVersion { get; }
    bool UpdateAvailable { get; }
    bool UpdateCheckSucceeded { get; }
    Uri SourceUri { get; }
    Task RefreshAsync(CancellationToken cancellationToken);
    Task CheckForUpdatesAsync(CancellationToken cancellationToken);
    Task<ModelManifest> DownloadLatestAsync(
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken);
    Task DeleteModelsAsync(CancellationToken cancellationToken);
}

public interface IDetectionRuntime
{
    string Provider { get; }
    bool IsReady { get; }
    event EventHandler<DetectionRuntimeMetrics>? MetricsChanged;
    event EventHandler<DetectionVisualizationFrame>? VisualizationChanged;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
