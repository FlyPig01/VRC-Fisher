using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using VrcFisher.Application;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Models;

/// <summary>
/// Owns the two-model installation as one versioned transaction.
/// </summary>
public sealed class ModelCatalog : IModelCatalog
{
    private static readonly string[] Required = ["locator.onnx", "minigame.onnx"];
    private const int MaximumDownloadAttempts = 3;
    private readonly DirectoryLayout _layout;
    private readonly HttpClient _httpClient;
    private readonly string? _repository;
    private readonly Uri _githubApiBase;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly object _sync = new();
    private IReadOnlyList<ModelStatus> _status = [];
    private bool _automaticAllowed;

    public ModelCatalog(
        DirectoryLayout layout,
        HttpClient httpClient,
        string? repository = null,
        Uri? githubApiBase = null)
    {
        _layout = layout;
        _httpClient = httpClient;
        _repository = repository ?? ReadRepository(layout.Root);
        _githubApiBase = (githubApiBase ?? new Uri("https://api.github.com/")).EnsureTrailingSlash();
    }

    public bool IsReady => GetStatus().Count == Required.Length
        && GetStatus().All(item => item.Installed && item.Valid);
    public bool AutomaticAllowed
    {
        get { lock (_sync) return _automaticAllowed && IsReady; }
    }

    public IReadOnlyList<ModelStatus> GetStatus()
    {
        lock (_sync) return _status;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        _layout.Ensure();
        var manifest = await ReadInstalledManifestAsync(cancellationToken);
        var statuses = new List<ModelStatus>(Required.Length);
        foreach (var fileName in Required)
        {
            var path = Path.Combine(_layout.Models, fileName);
            if (!File.Exists(path))
            {
                statuses.Add(new(fileName, false, false, 0, manifest?.Version, "文件不存在"));
                continue;
            }

            var info = new FileInfo(path);
            var expected = manifest?.Models.FirstOrDefault(item => item.FileName == fileName);
            var valid = expected is not null
                && info.Length == expected.Size
                && await HasSha256Async(path, expected.Sha256, cancellationToken);
            statuses.Add(new(
                fileName,
                true,
                valid,
                info.Length,
                manifest?.Version,
                valid ? "已安装并通过校验" : "存在但未通过清单校验"));
        }

        lock (_sync)
        {
            _status = statuses;
            _automaticAllowed = manifest?.AutomaticAllowed == true
                && statuses.All(item => item.Installed && item.Valid);
        }
    }

    public async Task<ModelManifest> DownloadLatestAsync(
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_repository))
            throw new InvalidOperationException("release.json 未配置模型仓库（owner/name）");

        await _operation.WaitAsync(cancellationToken);
        try
        {
            var source = await ResolveLatestManifestAsync(cancellationToken);
            return await DownloadManifestAsync(source.Manifest, source.ManifestUri, progress, cancellationToken);
        }
        finally
        {
            _operation.Release();
        }
    }

    // Kept for local tests and development mirrors that expose a manifest directly.
    public async Task<ModelManifest> DownloadLatestAsync(
        Uri manifestUri,
        CancellationToken cancellationToken,
        IProgress<ModelDownloadProgress>? progress = null)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            var manifest = await GetJsonAsync<ModelManifest>(manifestUri, null, cancellationToken)
                ?? throw new InvalidDataException("模型清单为空");
            ValidateManifest(manifest);
            return await DownloadManifestAsync(manifest, manifestUri, progress, cancellationToken);
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task DeleteModelsAsync(CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var fileName in Required.Append("installed-models.json"))
            {
                var path = Path.Combine(_layout.Models, fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            await RefreshAsync(cancellationToken);
        }
        finally
        {
            _operation.Release();
        }
    }

    private async Task<(ModelManifest Manifest, Uri ManifestUri)> ResolveLatestManifestAsync(
        CancellationToken cancellationToken)
    {
        var releasesUri = new Uri(
            _githubApiBase,
            $"repos/{_repository}/releases?per_page=100");
        var releases = await GetJsonAsync<List<GitHubRelease>>(
            releasesUri,
            "application/vnd.github+json",
            cancellationToken) ?? [];

        var candidates = releases
            .Where(item => !item.Draft && !item.Prerelease)
            .Select(item => (Release: item, Version: ParseModelVersion(item.TagName)))
            .Where(item => item.Version is not null)
            .OrderByDescending(item => item.Version)
            .ToArray();

        foreach (var candidate in candidates)
        {
            var manifestAsset = candidate.Release.Assets?
                .FirstOrDefault(item => string.Equals(item.Name, "model-manifest.json", StringComparison.Ordinal));
            if (manifestAsset?.BrowserDownloadUrl is null) continue;

            var manifestUri = new Uri(manifestAsset.BrowserDownloadUrl, UriKind.Absolute);
            var manifest = await GetJsonAsync<ModelManifest>(manifestUri, null, cancellationToken);
            if (manifest is null) continue;
            try { ValidateManifest(manifest); }
            catch (InvalidDataException) { continue; }
            if (!Version.TryParse(manifest.Version, out var manifestVersion)) continue;
            if (!candidate.Version!.Equals(manifestVersion)) continue;
            return (manifest, manifestUri);
        }

        throw new InvalidDataException("没有找到与当前 runtime_api 兼容的 models-v* Release");
    }

    private async Task<ModelManifest> DownloadManifestAsync(
        ModelManifest manifest,
        Uri manifestUri,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        _layout.Ensure();
        var staging = Path.Combine(_layout.Downloads, $"models-{Guid.NewGuid():N}");
        var backup = Path.Combine(_layout.Downloads, $"models-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var totalBytes = manifest.Models.Sum(item => item.Size);
        long completedBytes = 0;
        var completedFiles = 0;

        try
        {
            foreach (var model in manifest.Models)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(staging, model.FileName);
                var uri = new Uri(manifestUri, model.FileName);
                await DownloadFileAsync(
                    uri,
                    destination,
                    model,
                    completedBytes,
                    totalBytes,
                    completedFiles,
                    progress,
                    cancellationToken);
                completedBytes += model.Size;
                completedFiles++;
                progress?.Report(new(
                    model.FileName,
                    completedBytes,
                    totalBytes,
                    completedFiles,
                    manifest.Models.Count));
            }

            await File.WriteAllTextAsync(
                Path.Combine(staging, "installed-models.json"),
                JsonSerializer.Serialize(manifest, JsonOptions.Default),
                cancellationToken);

            ReplaceModelDirectory(staging, backup, cancellationToken);
            await RefreshAsync(CancellationToken.None);
            return manifest;
        }
        finally
        {
            DeleteDirectoryIfPresent(staging);
            DeleteDirectoryIfPresent(backup);
        }
    }

    private void ReplaceModelDirectory(string staging, string backup, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = _layout.Models;
        var movedOld = false;
        try
        {
            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
                movedOld = true;
            }
            Directory.Move(staging, target);
        }
        catch
        {
            if (movedOld && !Directory.Exists(target) && Directory.Exists(backup))
                Directory.Move(backup, target);
            throw;
        }
    }

    private async Task<ModelManifest?> ReadInstalledManifestAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_layout.Models, "installed-models.json");
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<ModelManifest>(
                stream,
                JsonOptions.Default,
                cancellationToken);
            if (manifest is not null) ValidateManifest(manifest);
            return manifest;
        }
        catch (JsonException) { return null; }
        catch (InvalidDataException) { return null; }
    }

    private static async Task<bool> HasSha256Async(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateManifest(ModelManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.RuntimeApi != 1 || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("模型清单版本不兼容");
        if (manifest.Models is null)
            throw new InvalidDataException("模型清单缺少 models");
        if (!Required.SequenceEqual(
                manifest.Models.Select(item => item.FileName).OrderBy(item => item, StringComparer.Ordinal)))
            throw new InvalidDataException("模型清单必须同时包含 locator.onnx 与 minigame.onnx");
        if (manifest.Models.Any(item =>
                item.Size <= 0
                || item.Sha256.Length != 64
                || item.Sha256.Any(character => !Uri.IsHexDigit(character))
                || !Required.Contains(item.FileName, StringComparer.Ordinal)))
            throw new InvalidDataException("模型清单包含无效文件大小、名称或 SHA-256");
    }

    private async Task DownloadFileAsync(
        Uri uri,
        string destination,
        ModelFileInfo expected,
        long completedBytes,
        long totalBytes,
        int completedFiles,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumDownloadAttempts; attempt++)
        {
            try
            {
                using var request = CreateRequest(uri, null);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (IsTransient(response.StatusCode) && attempt < MaximumDownloadAttempts)
                {
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                var buffer = new byte[1024 * 1024];
                long fileBytes = 0;
                int read;
                await using (var output = File.Create(destination))
                {
                    while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        fileBytes += read;
                        progress?.Report(new(
                            expected.FileName,
                            completedBytes + fileBytes,
                            totalBytes,
                            completedFiles,
                            Required.Length));
                    }
                    await output.FlushAsync(cancellationToken);
                }
                await VerifyAsync(destination, expected, cancellationToken);
                return;
            }
            catch (Exception error) when (IsTransient(error, cancellationToken)
                                          && attempt < MaximumDownloadAttempts)
            {
                if (File.Exists(destination)) File.Delete(destination);
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
        }

        throw new HttpRequestException($"模型下载在 {MaximumDownloadAttempts} 次尝试后仍失败：{expected.FileName}");
    }

    private async Task<T?> GetJsonAsync<T>(
        Uri uri,
        string? accept,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumDownloadAttempts; attempt++)
        {
            try
            {
                using var request = CreateRequest(uri, accept);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (IsTransient(response.StatusCode) && attempt < MaximumDownloadAttempts)
                {
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>(JsonOptions.Default, cancellationToken);
            }
            catch (Exception error) when (IsTransient(error, cancellationToken)
                                          && attempt < MaximumDownloadAttempts)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
        }

        throw new HttpRequestException($"请求在 {MaximumDownloadAttempts} 次尝试后仍失败：{uri}");
    }

    private static HttpRequestMessage CreateRequest(Uri uri, string? accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", "VRC-Fisher");
        if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);
        return request;
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static bool IsTransient(Exception error, CancellationToken cancellationToken) =>
        error is HttpRequestException or IOException
        || error is TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);

    private static async Task VerifyAsync(
        string path,
        ModelFileInfo expected,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length != expected.Size)
            throw new InvalidDataException($"模型大小不匹配：{expected.FileName}");
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(actual, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"模型 SHA-256 不匹配：{expected.FileName}");
    }

    private static Version? ParseModelVersion(string? tag)
    {
        if (tag is null || !tag.StartsWith("models-v", StringComparison.Ordinal)) return null;
        return Version.TryParse(tag["models-v".Length..], out var version) ? version : null;
    }

    private static string? ReadRepository(string root)
    {
        var path = Path.Combine(root, "release.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var repository = document.RootElement.TryGetProperty("repository", out var property)
                ? property.GetString()
                : null;
            return IsRepositoryName(repository) ? repository : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsRepositoryName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('/');
        return parts.Length == 2
            && parts.All(part => part.Length > 0 && part.All(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.'));
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] List<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl);
}

internal static class UriExtensions
{
    public static Uri EnsureTrailingSlash(this Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
}
