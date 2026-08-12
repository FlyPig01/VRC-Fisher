using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using VrcFisher.Application;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Models;

public sealed class ModelCatalog(DirectoryLayout layout, HttpClient httpClient) : IModelCatalog
{
    private static readonly string[] Required = ["locator.onnx", "minigame.onnx"];
    private readonly object _sync = new();
    private IReadOnlyList<ModelStatus> _status = [];

    public bool IsReady => GetStatus().All(item => item.Installed && item.Valid);

    public IReadOnlyList<ModelStatus> GetStatus()
    {
        lock (_sync) return _status;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        layout.Ensure();
        var statuses = new List<ModelStatus>();
        foreach (var fileName in Required)
        {
            var path = Path.Combine(layout.Models, fileName);
            if (!File.Exists(path))
            {
                statuses.Add(new(fileName, false, false, 0, null, "文件不存在"));
                continue;
            }

            var info = new FileInfo(path);
            var manifest = await ReadInstalledManifestAsync(cancellationToken);
            var expected = manifest?.Models.FirstOrDefault(item => item.FileName == fileName);
            var valid = expected is not null && info.Length == expected.Size && await HasSha256Async(path, expected.Sha256, cancellationToken);
            statuses.Add(new(fileName, true, valid, info.Length, manifest?.Version, valid ? "已安装并通过校验" : "存在但未通过清单校验"));
        }
        lock (_sync) _status = statuses;
    }

    public async Task<ModelManifest> DownloadLatestAsync(Uri manifestUri, CancellationToken cancellationToken)
    {
        layout.Ensure();
        var manifest = await httpClient.GetFromJsonAsync<ModelManifest>(manifestUri, JsonOptions.Default, cancellationToken)
            ?? throw new InvalidDataException("模型清单为空");
        ValidateManifest(manifest);
        var staging = Path.Combine(layout.Downloads, $"models-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var model in manifest.Models)
            {
                var uri = new Uri(manifestUri, model.FileName);
                var destination = Path.Combine(staging, model.FileName);
                await DownloadFileAsync(httpClient, uri, destination, cancellationToken);
                await VerifyAsync(destination, model, cancellationToken);
            }
            foreach (var model in manifest.Models)
            {
                var source = Path.Combine(staging, model.FileName);
                var target = Path.Combine(layout.Models, model.FileName);
                File.Move(source, target, overwrite: true);
            }
            await File.WriteAllTextAsync(
                Path.Combine(layout.Models, "installed-models.json"),
                JsonSerializer.Serialize(manifest, JsonOptions.Default),
                cancellationToken);
            await RefreshAsync(cancellationToken);
            return manifest;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public async Task DeleteModelsAsync(CancellationToken cancellationToken)
    {
        foreach (var fileName in Required.Append("installed-models.json"))
        {
            var path = Path.Combine(layout.Models, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        await RefreshAsync(cancellationToken);
    }

    private async Task<ModelManifest?> ReadInstalledManifestAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(layout.Models, "installed-models.json");
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<ModelManifest>(
                stream, JsonOptions.Default, cancellationToken);
            return manifest;
        }
        catch (JsonException) { return null; }
    }

    private static async Task<bool> HasSha256Async(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateManifest(ModelManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.RuntimeApi != 1 || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("模型清单版本不兼容");
        if (!Required.SequenceEqual(manifest.Models.Select(item => item.FileName).OrderBy(item => item, StringComparer.Ordinal)))
            throw new InvalidDataException("模型清单必须同时包含 locator.onnx 与 minigame.onnx");
        if (manifest.Models.Any(item => item.Size <= 0 || item.Sha256.Length != 64))
            throw new InvalidDataException("模型清单包含无效大小或 SHA-256");
    }

    private static async Task DownloadFileAsync(HttpClient client, Uri uri, string destination, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task VerifyAsync(string path, ModelFileInfo expected, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length != expected.Size) throw new InvalidDataException($"模型大小不匹配：{expected.FileName}");
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(actual, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"模型 SHA-256 不匹配：{expected.FileName}");
    }
}
