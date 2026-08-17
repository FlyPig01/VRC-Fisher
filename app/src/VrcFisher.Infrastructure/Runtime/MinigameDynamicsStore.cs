using System.Text.Json;
using VrcFisher.Application;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Runtime;

internal sealed record MinigameDynamicsFile(
    int SchemaVersion,
    double? ReleaseAcceleration,
    double? PressAcceleration,
    DateTimeOffset UpdatedAt);

internal sealed class MinigameDynamicsStore(string configDirectory)
{
    internal const int SchemaVersion = 2;
    private readonly string _path = Path.Combine(configDirectory, "minigame-dynamics.json");

    public MinigameDynamicsParameters Load()
    {
        if (!File.Exists(_path)) return MinigameDynamicsParameters.Empty;
        try
        {
            var file = JsonSerializer.Deserialize<MinigameDynamicsFile>(
                File.ReadAllText(_path),
                JsonOptions.Default);
            if (file is null) return MinigameDynamicsParameters.Empty;
            var parameters = file.SchemaVersion switch
            {
                SchemaVersion => new(
                    file.ReleaseAcceleration,
                    file.PressAcceleration),
                1 => new(
                    -file.ReleaseAcceleration,
                    -file.PressAcceleration),
                _ => MinigameDynamicsParameters.Empty
            };
            return parameters.Normalize();
        }
        catch (JsonException)
        {
            return MinigameDynamicsParameters.Empty;
        }
        catch (IOException)
        {
            return MinigameDynamicsParameters.Empty;
        }
    }

    public async Task SaveAsync(
        MinigameDynamicsParameters parameters,
        CancellationToken cancellationToken = default)
    {
        parameters = parameters.Normalize();
        if (parameters.ReleaseAcceleration is null
            && parameters.PressAcceleration is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var file = new MinigameDynamicsFile(
            SchemaVersion,
            parameters.ReleaseAcceleration,
            parameters.PressAcceleration,
            DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(
            _path,
            JsonSerializer.Serialize(file, JsonOptions.Default),
            cancellationToken);
    }
}
