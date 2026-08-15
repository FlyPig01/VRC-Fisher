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
            ToggleHotkey = HotkeyGestureRules.TryNormalize(ToggleHotkey, out var hotkey)
                ? hotkey
                : Default.ToggleHotkey,
            SettingsVersion = CurrentSettingsVersion
        };
    }
}

public static class HotkeyGestureRules
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var tokens = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;

        var ctrl = false;
        var alt = false;
        var shift = false;
        string? key = null;
        foreach (var token in tokens)
        {
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
            {
                if (ctrl) return false;
                ctrl = true;
            }
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                if (alt) return false;
                alt = true;
            }
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                if (shift) return false;
                shift = true;
            }
            else
            {
                if (key is not null) return false;
                key = token.ToUpperInvariant();
            }
        }

        if (key is null || !IsSupportedKey(key, out var functionKey)) return false;
        if (!functionKey && !ctrl && !alt && !shift) return false;
        if (alt && key == "F4") return false;

        var result = new List<string>(4);
        if (ctrl) result.Add("Ctrl");
        if (alt) result.Add("Alt");
        if (shift) result.Add("Shift");
        result.Add(key);
        normalized = string.Join('+', result);
        return true;
    }

    private static bool IsSupportedKey(string key, out bool functionKey)
    {
        functionKey = key.Length is 2 or 3
            && key[0] == 'F'
            && int.TryParse(key.AsSpan(1), out var number)
            && number is >= 1 and <= 24;
        return functionKey
            || key.Length == 1 && (key[0] is >= 'A' and <= 'Z' || key[0] is >= '0' and <= '9');
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
    event EventHandler? StatusChanged;
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
    ExecutionRuntimeInfo Execution { get; }
    bool IsReady { get; }
    event EventHandler<DetectionRuntimeMetrics>? MetricsChanged;
    event EventHandler<DetectionVisualizationFrame>? VisualizationChanged;
    Task PrepareAsync(CancellationToken cancellationToken);
    void Activate();
    Task StopAsync(CancellationToken cancellationToken);
}
