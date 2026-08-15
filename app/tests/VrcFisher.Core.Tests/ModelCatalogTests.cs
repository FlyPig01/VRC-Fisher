using System.Net;
using System.Security.Cryptography;
using System.Text;
using VrcFisher.Application;
using VrcFisher.Core;
using VrcFisher.Infrastructure.Models;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class ModelCatalogTests
{
    private static readonly byte[] ModelCard = Encoding.UTF8.GetBytes("model card");
    private static readonly byte[] ModelLicense = Encoding.UTF8.GetBytes("AGPL license");

    [Fact]
    public void Empty_catalog_is_not_ready_before_refresh()
    {
        using var temporary = new TemporaryDirectory();
        var catalog = new ModelCatalog(
            new DirectoryLayout(temporary.Path),
            new HttpClient(new StaticHttpHandler([])));

        Assert.False(catalog.IsReady);
        Assert.False(catalog.AutomaticAllowed);
    }

    [Fact]
    public async Task Invalid_download_keeps_the_previous_model_set()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new DirectoryLayout(temporary.Path);
        layout.Ensure();
        var oldLocator = Encoding.UTF8.GetBytes("old-locator");
        var oldMinigame = Encoding.UTF8.GetBytes("old-minigame");
        await File.WriteAllBytesAsync(Path.Combine(layout.Models, "locator.onnx"), oldLocator);
        await File.WriteAllBytesAsync(Path.Combine(layout.Models, "minigame.onnx"), oldMinigame);
        await File.WriteAllBytesAsync(Path.Combine(layout.Models, "MODEL_CARD.md"), ModelCard);
        await File.WriteAllBytesAsync(Path.Combine(layout.Models, "MODEL_LICENSE.txt"), ModelLicense);
        await File.WriteAllTextAsync(
            Path.Combine(layout.Models, "installed-models.json"),
            Manifest("old", oldLocator, oldMinigame));

        var newLocator = Encoding.UTF8.GetBytes("new-locator");
        var newMinigame = Encoding.UTF8.GetBytes("new-minigame");
        var invalidManifest = Manifest("new", newLocator, newMinigame, overrideMinigameHash: "0".PadLeft(64, '0'));
        var handler = new StaticHttpHandler(new Dictionary<string, byte[]>
        {
            ["https://test.invalid/model-manifest.json"] = Encoding.UTF8.GetBytes(invalidManifest),
            ["https://test.invalid/locator.onnx"] = newLocator,
            ["https://test.invalid/minigame.onnx"] = newMinigame,
            ["https://test.invalid/MODEL_CARD.md"] = ModelCard,
            ["https://test.invalid/MODEL_LICENSE.txt"] = ModelLicense
        });
        var catalog = new ModelCatalog(layout, new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidDataException>(() => catalog.DownloadLatestAsync(
            new Uri("https://test.invalid/model-manifest.json"),
            CancellationToken.None));

        Assert.Equal(oldLocator, await File.ReadAllBytesAsync(Path.Combine(layout.Models, "locator.onnx")));
        Assert.Equal(oldMinigame, await File.ReadAllBytesAsync(Path.Combine(layout.Models, "minigame.onnx")));
        Assert.Equal(ModelCard, await File.ReadAllBytesAsync(Path.Combine(layout.Models, "MODEL_CARD.md")));
        Assert.Equal(ModelLicense, await File.ReadAllBytesAsync(Path.Combine(layout.Models, "MODEL_LICENSE.txt")));
        Assert.DoesNotContain(Directory.EnumerateDirectories(layout.Downloads), path =>
            Path.GetFileName(path).StartsWith("models-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Latest_models_release_is_resolved_from_github_metadata()
    {
        using var temporary = new TemporaryDirectory();
        var locator = Encoding.UTF8.GetBytes("locator");
        var minigame = Encoding.UTF8.GetBytes("minigame");
        var manifest = Manifest("1.2.0", locator, minigame);
        var handler = new StaticHttpHandler(new Dictionary<string, byte[]>
        {
            ["https://api.test/repos/example/project/releases?per_page=100"] = Encoding.UTF8.GetBytes("""
            [{"tag_name":"models-v1.2.0","draft":false,"prerelease":false,"assets":[
              {"name":"model-manifest.json","browser_download_url":"https://cdn.test/model-manifest.json"},
              {"name":"locator.onnx","browser_download_url":"https://cdn.test/locator.onnx"},
              {"name":"minigame.onnx","browser_download_url":"https://cdn.test/minigame.onnx"},
              {"name":"MODEL_CARD.md","browser_download_url":"https://cdn.test/MODEL_CARD.md"},
              {"name":"MODEL_LICENSE.txt","browser_download_url":"https://cdn.test/MODEL_LICENSE.txt"}
            ]}]
            """),
            ["https://cdn.test/model-manifest.json"] = Encoding.UTF8.GetBytes(manifest),
            ["https://cdn.test/locator.onnx"] = locator,
            ["https://cdn.test/minigame.onnx"] = minigame,
            ["https://cdn.test/MODEL_CARD.md"] = ModelCard,
            ["https://cdn.test/MODEL_LICENSE.txt"] = ModelLicense
        });
        var catalog = new ModelCatalog(
            new DirectoryLayout(temporary.Path),
            new HttpClient(handler),
            "example/project",
            new Uri("https://api.test/"));

        var installed = await catalog.DownloadLatestAsync(progress: null, CancellationToken.None);

        Assert.Equal("1.2.0", installed.Version);
        Assert.True(File.Exists(Path.Combine(temporary.Path, "models", "locator.onnx")));
        Assert.True(File.Exists(Path.Combine(temporary.Path, "models", "minigame.onnx")));
        Assert.True(File.Exists(Path.Combine(temporary.Path, "models", "MODEL_CARD.md")));
        Assert.True(File.Exists(Path.Combine(temporary.Path, "models", "MODEL_LICENSE.txt")));
    }

    [Fact]
    public async Task Update_check_compares_installed_and_latest_model_versions()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new DirectoryLayout(temporary.Path);
        layout.Ensure();
        var locator = Encoding.UTF8.GetBytes("locator");
        var minigame = Encoding.UTF8.GetBytes("minigame");
        await File.WriteAllBytesAsync(Path.Combine(layout.Models, "locator.onnx"), locator);
        await File.WriteAllBytesAsync(Path.Combine(layout.Models, "minigame.onnx"), minigame);
        await File.WriteAllBytesAsync(Path.Combine(layout.Models, "MODEL_CARD.md"), ModelCard);
        await File.WriteAllBytesAsync(Path.Combine(layout.Models, "MODEL_LICENSE.txt"), ModelLicense);
        await File.WriteAllTextAsync(
            Path.Combine(layout.Models, "installed-models.json"),
            Manifest("1.0.0", locator, minigame));

        var latestManifest = Manifest("1.1.0", locator, minigame);
        var handler = new StaticHttpHandler(new Dictionary<string, byte[]>
        {
            ["https://api.test/repos/example/project/releases?per_page=100"] = Encoding.UTF8.GetBytes("""
            [{"tag_name":"models-v1.1.0","draft":false,"prerelease":false,"assets":[
              {"name":"model-manifest.json","browser_download_url":"https://cdn.test/model-manifest.json"}
            ]}]
            """),
            ["https://cdn.test/model-manifest.json"] = Encoding.UTF8.GetBytes(latestManifest)
        });
        var catalog = new ModelCatalog(
            layout,
            new HttpClient(handler),
            "example/project",
            new Uri("https://api.test/"));

        await catalog.RefreshAsync(CancellationToken.None);
        await catalog.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal("1.0.0", catalog.InstalledVersion);
        Assert.Equal("1.1.0", catalog.LatestVersion);
        Assert.True(catalog.UpdateAvailable);
        Assert.True(catalog.UpdateCheckSucceeded);
    }

    [Fact]
    public async Task Transient_model_download_is_retried()
    {
        using var temporary = new TemporaryDirectory();
        var locator = Encoding.UTF8.GetBytes("locator");
        var minigame = Encoding.UTF8.GetBytes("minigame");
        var manifest = Manifest("1.0.0", locator, minigame);
        var handler = new TransientHttpHandler(new Dictionary<string, byte[]>
        {
            ["https://test.invalid/model-manifest.json"] = Encoding.UTF8.GetBytes(manifest),
            ["https://test.invalid/locator.onnx"] = locator,
            ["https://test.invalid/minigame.onnx"] = minigame,
            ["https://test.invalid/MODEL_CARD.md"] = ModelCard,
            ["https://test.invalid/MODEL_LICENSE.txt"] = ModelLicense
        }, "https://test.invalid/locator.onnx");
        var catalog = new ModelCatalog(
            new DirectoryLayout(temporary.Path),
            new HttpClient(handler));

        await catalog.DownloadLatestAsync(
            new Uri("https://test.invalid/model-manifest.json"),
            CancellationToken.None);

        Assert.Equal(2, handler.TransientRequestCount);
        Assert.Equal(locator, await File.ReadAllBytesAsync(
            Path.Combine(temporary.Path, "models", "locator.onnx")));
    }

    [Fact]
    public async Task Documentation_is_verified_and_deleted_with_the_models()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new DirectoryLayout(temporary.Path);
        var locator = Encoding.UTF8.GetBytes("locator");
        var minigame = Encoding.UTF8.GetBytes("minigame");
        var manifest = Manifest("1.0.0", locator, minigame);
        var handler = new StaticHttpHandler(new Dictionary<string, byte[]>
        {
            ["https://test.invalid/model-manifest.json"] = Encoding.UTF8.GetBytes(manifest),
            ["https://test.invalid/locator.onnx"] = locator,
            ["https://test.invalid/minigame.onnx"] = minigame,
            ["https://test.invalid/MODEL_CARD.md"] = ModelCard,
            ["https://test.invalid/MODEL_LICENSE.txt"] = ModelLicense
        });
        var catalog = new ModelCatalog(layout, new HttpClient(handler));

        await catalog.DownloadLatestAsync(
            new Uri("https://test.invalid/model-manifest.json"),
            CancellationToken.None);

        Assert.True(catalog.IsReady);
        Assert.Equal(ModelCard, await File.ReadAllBytesAsync(Path.Combine(layout.Models, "MODEL_CARD.md")));
        Assert.Equal(ModelLicense, await File.ReadAllBytesAsync(Path.Combine(layout.Models, "MODEL_LICENSE.txt")));

        await File.WriteAllTextAsync(Path.Combine(layout.Models, "MODEL_CARD.md"), "tampered");
        await catalog.RefreshAsync(CancellationToken.None);
        Assert.False(catalog.IsReady);
        Assert.All(catalog.GetStatus(), item => Assert.Contains("模型卡或许可证", item.Message));

        await catalog.DeleteModelsAsync(CancellationToken.None);

        foreach (var fileName in new[]
                 {
                     "locator.onnx",
                     "minigame.onnx",
                     "MODEL_CARD.md",
                     "MODEL_LICENSE.txt",
                     "installed-models.json"
                 })
            Assert.False(File.Exists(Path.Combine(layout.Models, fileName)));
    }

    [Fact]
    public async Task Manifest_without_required_documentation_is_rejected()
    {
        using var temporary = new TemporaryDirectory();
        var locator = Encoding.UTF8.GetBytes("locator");
        var minigame = Encoding.UTF8.GetBytes("minigame");
        var manifest = Manifest("1.0.0", locator, minigame)
            .Replace("\"documentation\"", "\"optional_documentation\"", StringComparison.Ordinal);
        var handler = new StaticHttpHandler(new Dictionary<string, byte[]>
        {
            ["https://test.invalid/model-manifest.json"] = Encoding.UTF8.GetBytes(manifest)
        });
        var catalog = new ModelCatalog(
            new DirectoryLayout(temporary.Path),
            new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidDataException>(() => catalog.DownloadLatestAsync(
            new Uri("https://test.invalid/model-manifest.json"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Interrupted_download_resumes_from_persistent_partial_file()
    {
        using var temporary = new TemporaryDirectory();
        var layout = new DirectoryLayout(temporary.Path);
        var locator = Encoding.UTF8.GetBytes("locator-data");
        var minigame = Encoding.UTF8.GetBytes("minigame-data");
        var manifest = Manifest("1.0.0", locator, minigame);
        var files = new Dictionary<string, byte[]>
        {
            ["https://test.invalid/model-manifest.json"] = Encoding.UTF8.GetBytes(manifest),
            ["https://test.invalid/locator.onnx"] = locator,
            ["https://test.invalid/minigame.onnx"] = minigame,
            ["https://test.invalid/MODEL_CARD.md"] = ModelCard,
            ["https://test.invalid/MODEL_LICENSE.txt"] = ModelLicense
        };
        var interrupted = new RangeHttpHandler(files, "https://test.invalid/locator.onnx", failAfterBytes: 3);
        var firstCatalog = new ModelCatalog(layout, new HttpClient(interrupted));

        await Assert.ThrowsAsync<IOException>(() => firstCatalog.DownloadLatestAsync(
            new Uri("https://test.invalid/model-manifest.json"),
            CancellationToken.None));

        var partial = Directory.EnumerateFiles(
                layout.Downloads,
                "locator.onnx.part",
                SearchOption.AllDirectories)
            .Single();
        var partialLength = new FileInfo(partial).Length;
        Assert.InRange(partialLength, 1, locator.Length - 1);

        var resumed = new RangeHttpHandler(files, "https://test.invalid/locator.onnx");
        var secondCatalog = new ModelCatalog(layout, new HttpClient(resumed));
        await secondCatalog.DownloadLatestAsync(
            new Uri("https://test.invalid/model-manifest.json"),
            CancellationToken.None);

        Assert.Contains(partialLength, resumed.RangeStarts);
        Assert.Equal(locator, await File.ReadAllBytesAsync(Path.Combine(layout.Models, "locator.onnx")));
        Assert.True(secondCatalog.IsReady);
        Assert.Empty(Directory.EnumerateDirectories(
            layout.Downloads,
            "models-download-*",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task New_release_published_during_download_restarts_with_the_new_manifest()
    {
        using var temporary = new TemporaryDirectory();
        var oldLocator = Encoding.UTF8.GetBytes("old-locator");
        var oldMinigame = Encoding.UTF8.GetBytes("old-minigame");
        var newLocator = Encoding.UTF8.GetBytes("new-locator");
        var newMinigame = Encoding.UTF8.GetBytes("new-minigame");
        var handler = new ChangingReleaseHttpHandler(
            Manifest("1.0.0", oldLocator, oldMinigame),
            Manifest("1.1.0", newLocator, newMinigame),
            oldLocator,
            oldMinigame,
            newLocator,
            newMinigame);
        var catalog = new ModelCatalog(
            new DirectoryLayout(temporary.Path),
            new HttpClient(handler),
            "example/project",
            new Uri("https://api.test/"));

        var installed = await catalog.DownloadLatestAsync(progress: null, CancellationToken.None);

        Assert.Equal("1.1.0", installed.Version);
        Assert.Equal(newLocator, await File.ReadAllBytesAsync(
            Path.Combine(temporary.Path, "models", "locator.onnx")));
        Assert.Equal(newMinigame, await File.ReadAllBytesAsync(
            Path.Combine(temporary.Path, "models", "minigame.onnx")));
        Assert.True(handler.ReleaseRequestCount >= 4);
    }

    private static string Manifest(
        string version,
        byte[] locator,
        byte[] minigame,
        string? overrideMinigameHash = null)
    {
        var locatorHash = Convert.ToHexString(SHA256.HashData(locator)).ToLowerInvariant();
        var minigameHash = overrideMinigameHash
            ?? Convert.ToHexString(SHA256.HashData(minigame)).ToLowerInvariant();
        var modelCardHash = Convert.ToHexString(SHA256.HashData(ModelCard)).ToLowerInvariant();
        var modelLicenseHash = Convert.ToHexString(SHA256.HashData(ModelLicense)).ToLowerInvariant();
        return $$"""
        {
          "schema_version": 2,
          "runtime_api": 1,
          "version": "{{version}}",
          "models": [
            {"filename":"locator.onnx","size":{{locator.Length}},"sha256":"{{locatorHash}}"},
            {"filename":"minigame.onnx","size":{{minigame.Length}},"sha256":"{{minigameHash}}"}
          ],
          "documentation": [
            {"filename":"MODEL_CARD.md","size":{{ModelCard.Length}},"sha256":"{{modelCardHash}}"},
            {"filename":"MODEL_LICENSE.txt","size":{{ModelLicense.Length}},"sha256":"{{modelLicenseHash}}"}
          ]
        }
        """;
    }

    private sealed class StaticHttpHandler(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!files.TryGetValue(request.RequestUri!.ToString(), out var bytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }

    private sealed class TransientHttpHandler(
        Dictionary<string, byte[]> files,
        string transientUri) : HttpMessageHandler
    {
        public int TransientRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            if (uri == transientUri)
            {
                TransientRequestCount++;
                if (TransientRequestCount == 1)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            if (!files.TryGetValue(uri, out var bytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }

    private sealed class RangeHttpHandler(
        Dictionary<string, byte[]> files,
        string rangedUri,
        int? failAfterBytes = null) : HttpMessageHandler
    {
        private readonly object _sync = new();
        public List<long> RangeStarts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            if (!files.TryGetValue(uri, out var bytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var start = request.Headers.Range?.Ranges.Single().From ?? 0;
            if (uri == rangedUri)
            {
                lock (_sync) RangeStarts.Add(start);
            }
            var remaining = bytes[(int)start..];
            Stream stream = uri == rangedUri && failAfterBytes is not null
                ? new FailingReadStream(remaining, failAfterBytes.Value)
                : new MemoryStream(remaining, writable: false);
            var response = new HttpResponseMessage(start > 0
                ? HttpStatusCode.PartialContent
                : HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            };
            if (start > 0)
                response.Content.Headers.ContentRange = new(start, bytes.Length - 1, bytes.Length);
            return Task.FromResult(response);
        }
    }

    private sealed class FailingReadStream(byte[] bytes, int failAfterBytes)
        : MemoryStream(bytes, writable: false)
    {
        private int _remaining = failAfterBytes;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_remaining <= 0) throw new IOException("simulated connection loss");
            var requested = Math.Min(buffer.Length, _remaining);
            var read = Read(buffer.Span[..requested]);
            _remaining -= read;
            return ValueTask.FromResult(read);
        }
    }

    private sealed class ChangingReleaseHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly byte[] _oldRelease;
        private readonly byte[] _newRelease;
        private int _releaseRequestCount;

        public ChangingReleaseHttpHandler(
            string oldManifest,
            string newManifest,
            byte[] oldLocator,
            byte[] oldMinigame,
            byte[] newLocator,
            byte[] newMinigame)
        {
            _oldRelease = Encoding.UTF8.GetBytes(Release("1.0.0", "https://cdn.test/old/model-manifest.json"));
            _newRelease = Encoding.UTF8.GetBytes(Release("1.1.0", "https://cdn.test/new/model-manifest.json"));
            _files = new Dictionary<string, byte[]>
            {
                ["https://cdn.test/old/model-manifest.json"] = Encoding.UTF8.GetBytes(oldManifest),
                ["https://cdn.test/old/locator.onnx"] = oldLocator,
                ["https://cdn.test/old/minigame.onnx"] = oldMinigame,
                ["https://cdn.test/old/MODEL_CARD.md"] = ModelCard,
                ["https://cdn.test/old/MODEL_LICENSE.txt"] = ModelLicense,
                ["https://cdn.test/new/model-manifest.json"] = Encoding.UTF8.GetBytes(newManifest),
                ["https://cdn.test/new/locator.onnx"] = newLocator,
                ["https://cdn.test/new/minigame.onnx"] = newMinigame,
                ["https://cdn.test/new/MODEL_CARD.md"] = ModelCard,
                ["https://cdn.test/new/MODEL_LICENSE.txt"] = ModelLicense
            };
        }

        public int ReleaseRequestCount => Volatile.Read(ref _releaseRequestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            byte[]? bytes;
            if (uri == "https://api.test/repos/example/project/releases?per_page=100")
            {
                var count = Interlocked.Increment(ref _releaseRequestCount);
                bytes = count == 1 ? _oldRelease : _newRelease;
            }
            else
            {
                _files.TryGetValue(uri, out bytes);
            }
            return Task.FromResult(bytes is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                });
        }

        private static string Release(string version, string manifestUrl) => $$"""
        [{"tag_name":"models-v{{version}}","draft":false,"prerelease":false,"assets":[
          {"name":"model-manifest.json","browser_download_url":"{{manifestUrl}}"}
        ]}]
        """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"vrc-fisher-test-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
