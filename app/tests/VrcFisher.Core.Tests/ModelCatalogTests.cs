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
