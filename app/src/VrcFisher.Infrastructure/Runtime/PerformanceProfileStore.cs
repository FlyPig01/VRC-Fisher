using System.Text.Json;
using Microsoft.Win32;
using VrcFisher.Application;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Runtime;

public sealed record PerformanceProfileIdentity(
    string Provider,
    string Hardware,
    string ModelVersion,
    int CaptureWidth,
    int CaptureHeight);

public sealed record InferencePerformanceProfile(
    PerformanceProfileIdentity Identity,
    int LocatorIntervalMs,
    int HookingIntervalMs,
    int MinigameIntervalMs,
    int PanelRecheckIntervalMs,
    double? LocatorP95Ms,
    double? LocatorAndMinigameP95Ms,
    double? CachedMinigameP95Ms,
    DateTimeOffset UpdatedAt);

public sealed record PerformanceProfileFile(
    int SchemaVersion,
    IReadOnlyList<InferencePerformanceProfile> Profiles);

public sealed class PerformanceProfileStore(string configDirectory)
{
    public const int SchemaVersion = 1;
    private const int MaximumProfiles = 16;
    private readonly string _path = Path.Combine(configDirectory, "performance-profiles.json");

    public InferencePerformanceProfile? Load(PerformanceProfileIdentity identity)
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var file = JsonSerializer.Deserialize<PerformanceProfileFile>(
                File.ReadAllText(_path),
                JsonOptions.Default);
            if (file?.SchemaVersion != SchemaVersion || file.Profiles is null) return null;
            return file.Profiles.FirstOrDefault(item => item.Identity == identity);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        InferencePerformanceProfile profile,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var profiles = LoadAll()
            .Where(item => item.Identity != profile.Identity)
            .Append(profile)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(MaximumProfiles)
            .ToArray();
        var json = JsonSerializer.Serialize(
            new PerformanceProfileFile(SchemaVersion, profiles),
            JsonOptions.Default);
        await File.WriteAllTextAsync(_path, json, cancellationToken);
    }

    public static PerformanceProfileIdentity CreateIdentity(
        string provider,
        IModelCatalog modelCatalog,
        int captureWidth,
        int captureHeight)
    {
        var versions = string.Join(
            ";",
            modelCatalog.GetStatus()
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => $"{item.Name}:{item.Version ?? item.Size.ToString()}") );
        return new PerformanceProfileIdentity(
            provider,
            HardwareIdentity.Read(provider),
            versions,
            captureWidth,
            captureHeight);
    }

    private IReadOnlyList<InferencePerformanceProfile> LoadAll()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var file = JsonSerializer.Deserialize<PerformanceProfileFile>(
                File.ReadAllText(_path),
                JsonOptions.Default);
            return file?.SchemaVersion == SchemaVersion && file.Profiles is not null
                ? file.Profiles
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static class HardwareIdentity
    {
        public static string Read(string provider)
        {
            var cpu = ReadRegistryValue(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                    "ProcessorNameString")
                ?? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
                ?? $"{Environment.ProcessorCount}-logical-processors";
            if (!provider.Contains("Dml", StringComparison.OrdinalIgnoreCase))
                return $"CPU:{cpu.Trim()}";

            var adapters = ReadDisplayAdapters();
            return $"GPU:{adapters};CPU:{cpu.Trim()}";
        }

        private static string ReadDisplayAdapters()
        {
            try
            {
                using var map = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\VIDEO");
                if (map is null) return "DirectML-device-0";
                var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in map.GetValueNames())
                {
                    if (map.GetValue(name) is not string registryPath) continue;
                    const string machinePrefix = @"\Registry\Machine\";
                    if (!registryPath.StartsWith(machinePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    using var adapter = Registry.LocalMachine.OpenSubKey(registryPath[machinePrefix.Length..]);
                    var description = adapter?.GetValue("DriverDesc") as string
                        ?? adapter?.GetValue("HardwareInformation.AdapterString") as string;
                    if (!string.IsNullOrWhiteSpace(description)) values.Add(description.Trim());
                }
                return values.Count == 0 ? "DirectML-device-0" : string.Join("+", values);
            }
            catch (Exception error) when (error is UnauthorizedAccessException
                                          or IOException
                                          or System.Security.SecurityException)
            {
                return "DirectML-device-0";
            }
        }

        private static string? ReadRegistryValue(string keyPath, string valueName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                return key?.GetValue(valueName) as string;
            }
            catch (Exception error) when (error is UnauthorizedAccessException
                                          or IOException
                                          or System.Security.SecurityException)
            {
                return null;
            }
        }
    }
}
