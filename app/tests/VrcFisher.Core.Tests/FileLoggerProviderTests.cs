using Microsoft.Extensions.Logging;
using VrcFisher.Core;
using VrcFisher.Infrastructure.Logging;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class FileLoggerProviderTests
{
    [Fact]
    public void Run_mode_filters_debug_and_dispose_flushes_current_log()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using (var provider = new FileLoggerProvider(root, ApplicationMode.Run))
            {
                var logger = provider.CreateLogger("test");
                logger.LogDebug("debug-event");
                logger.LogInformation("run-event");
            }

            var text = File.ReadAllText(Path.Combine(root, "run", "current.log"));
            Assert.Contains("run-event", text);
            Assert.Contains("thread_id=", text);
            Assert.DoesNotContain("debug-event", text);
            Assert.False(File.Exists(Path.Combine(root, "debug", "current.log")));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void SetMode_routes_subsequent_events_to_debug_log_immediately()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using (var provider = new FileLoggerProvider(root, ApplicationMode.Run))
            {
                var logger = provider.CreateLogger("test");
                logger.LogInformation("before-switch");
                provider.SetMode(ApplicationMode.Debug);
                logger.LogDebug("after-switch");
            }

            Assert.Contains("before-switch", File.ReadAllText(Path.Combine(root, "run", "current.log")));
            Assert.Contains("after-switch", File.ReadAllText(Path.Combine(root, "debug", "current.log")));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void New_process_rotates_current_log_to_history()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using (var first = new FileLoggerProvider(root, ApplicationMode.Run))
                first.CreateLogger("test").LogInformation("first-session");

            using (var second = new FileLoggerProvider(root, ApplicationMode.Run))
                second.CreateLogger("test").LogInformation("second-session");

            var history = File.ReadAllText(Path.Combine(root, "run", "history.log"));
            var current = File.ReadAllText(Path.Combine(root, "run", "current.log"));
            Assert.Contains("first-session", history);
            Assert.DoesNotContain("second-session", history);
            Assert.Contains("second-session", current);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void Legacy_log_is_migrated_then_removed()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var legacy = Path.Combine(root, "vrc-fisher.log");
            File.WriteAllText(legacy, "legacy-entry" + Environment.NewLine);

            using var provider = new FileLoggerProvider(root, ApplicationMode.Run);

            Assert.False(File.Exists(legacy));
            Assert.Contains("legacy-entry", File.ReadAllText(Path.Combine(root, "run", "history.log")));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void Rotation_trims_oversized_debug_history_to_one_mebibyte()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var directory = Path.Combine(root, "debug");
            Directory.CreateDirectory(directory);
            var current = Path.Combine(directory, "current.log");
            using (var writer = new StreamWriter(current))
            {
                var payload = new string('x', 900);
                for (var index = 0; index < 3000; index++)
                    writer.WriteLine($"entry={index:D4} {payload}");
            }

            using var provider = new FileLoggerProvider(root, ApplicationMode.Debug);

            var history = Path.Combine(directory, "history.log");
            Assert.InRange(new FileInfo(history).Length, 1, 1024 * 1024);
            Assert.Contains("entry=2999", File.ReadAllText(history));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void Saturated_normal_queue_keeps_latest_debug_event_and_priority_error()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using (var provider = new FileLoggerProvider(root, ApplicationMode.Debug))
            {
                var logger = provider.CreateLogger("test");
                for (var index = 0; index < 4000; index++)
                    logger.LogDebug("entry={Index} payload={Payload}", index, new string('x', 128));
                logger.LogError("priority-error");
            }

            var text = File.ReadAllText(Path.Combine(root, "debug", "current.log"));
            Assert.Contains("entry=3999", text);
            Assert.Contains("priority-error", text);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void Active_debug_log_is_trimmed_and_keeps_its_latest_complete_lines()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using (var provider = new FileLoggerProvider(root, ApplicationMode.Debug))
            {
                var logger = provider.CreateLogger("test");
                var payload = new string('x', 800);
                for (var batch = 0; batch < 40; batch++)
                {
                    for (var item = 0; item < 100; item++)
                        logger.LogDebug("entry={Entry} payload={Payload}", batch * 100 + item, payload);
                    Thread.Sleep(10);
                }
            }

            var current = Path.Combine(root, "debug", "current.log");
            Assert.InRange(new FileInfo(current).Length, 1, 1024 * 1024);
            Assert.Contains("entry=3999", File.ReadAllText(current));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [Fact]
    public void Invalid_log_root_does_not_escape_into_business_code()
    {
        var parent = CreateTemporaryDirectory();
        try
        {
            var root = Path.Combine(parent, "not-a-directory");
            File.WriteAllText(root, "occupied");

            using var provider = new FileLoggerProvider(root, ApplicationMode.Debug);
            var exception = Record.Exception(() =>
                provider.CreateLogger("test").LogError("write-must-not-throw"));

            Assert.Null(exception);
        }
        finally
        {
            DeleteTemporaryDirectory(parent);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "vrc-fisher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
