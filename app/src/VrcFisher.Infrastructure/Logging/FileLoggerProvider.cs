using Microsoft.Extensions.Logging;

namespace VrcFisher.Infrastructure.Logging;

public sealed class FileLoggerProvider(string filePath) : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer = Open(filePath);

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _sync, _writer);

    public void Dispose()
    {
        lock (_sync)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }

    private static StreamWriter Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new StreamWriter(path, append: true) { AutoFlush = true };
    }

    private sealed class FileLogger(string category, object sync, StreamWriter writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"{DateTimeOffset.Now:O} [{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            lock (sync) writer.WriteLine(line);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
