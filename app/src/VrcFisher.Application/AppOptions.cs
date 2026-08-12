using System.Text.Json;
using VrcFisher.Core;

namespace VrcFisher.Application;

public sealed record AppOptions(
    string Language = "zh-CN",
    ExecutionDevice Device = ExecutionDevice.Auto,
    bool AutomaticMode = false,
    double ConfidenceThreshold = 0.35,
    double IoUThreshold = 0.45,
    int InputSize = 640,
    string? CaptureDisplay = null)
{
    public static AppOptions Default => new();
}

public sealed class OptionsStore(string rootDirectory)
{
    private readonly string _path = Path.Combine(rootDirectory, "config", "user.json");

    public AppOptions Load()
    {
        if (!File.Exists(_path)) return AppOptions.Default;
        try
        {
            var value = JsonSerializer.Deserialize<AppOptions>(File.ReadAllText(_path), JsonOptions.Default);
            return value ?? AppOptions.Default;
        }
        catch (JsonException)
        {
            return AppOptions.Default;
        }
    }

    public async Task SaveAsync(AppOptions options, CancellationToken cancellationToken = default)
    {
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
    public string Artifacts => Path.Combine(Root, "artifacts");

    public void Ensure()
    {
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(Models);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Artifacts);
    }
}

public interface IModelCatalog
{
    IReadOnlyList<ModelStatus> GetStatus();
    bool IsReady { get; }
    Task RefreshAsync(CancellationToken cancellationToken);
    Task DeleteModelsAsync(CancellationToken cancellationToken);
}

public interface IDetectionRuntime
{
    string Provider { get; }
    bool IsReady { get; }
    event EventHandler<DetectionRuntimeMetrics>? MetricsChanged;
    Task StartAsync(bool automatic, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
