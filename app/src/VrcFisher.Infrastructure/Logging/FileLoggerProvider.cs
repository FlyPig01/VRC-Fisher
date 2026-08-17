using System.Text;
using Microsoft.Extensions.Logging;
using VrcFisher.Core;

namespace VrcFisher.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int NormalQueueLimit = 512;
    private const int PriorityQueueLimit = 32;
    private const int FlushThresholdBytes = 32 * 1024;
    private const int NormalLineLimitBytes = 1024;
    private const int ExceptionLineLimitBytes = 8 * 1024;
    private const long RunFileLimitBytes = 256 * 1024;
    private const long DebugFileLimitBytes = 1024 * 1024;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly int NewLineBytes = Utf8.GetByteCount(Environment.NewLine);

    private readonly object _queueSync = new();
    private readonly LinkedList<LogEntry> _normalQueue = new();
    private readonly Queue<LogEntry> _priorityQueue = new();
    private readonly SemaphoreSlim _wakeWriter = new(0, 1);
    private readonly ModeFiles _runFiles;
    private readonly ModeFiles _debugFiles;
    private readonly Task _writerTask;
    private int _mode;
    private int _disposeStarted;
    private int _queuedNormalBytes;
    private long _sequence;

    public FileLoggerProvider(string logsDirectory, ApplicationMode initialMode)
    {
        var root = Path.GetFullPath(logsDirectory);
        _runFiles = new ModeFiles(Path.Combine(root, "run"), RunFileLimitBytes);
        _debugFiles = new ModeFiles(Path.Combine(root, "debug"), DebugFileLimitBytes);
        _mode = NormalizeMode(initialMode);

        TryInitializeFiles(root);
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void SetMode(ApplicationMode mode) =>
        Volatile.Write(ref _mode, NormalizeMode(mode));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        WakeWriter();
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Logging must never prevent the application from shutting down.
        }
    }

    private bool IsEnabled(LogLevel level)
    {
        if (level is LogLevel.None or LogLevel.Trace) return false;
        var mode = (ApplicationMode)Volatile.Read(ref _mode);
        return mode == ApplicationMode.Debug || level >= LogLevel.Information;
    }

    private void Enqueue(
        string category,
        LogLevel level,
        string message,
        Exception? exception)
    {
        if (Volatile.Read(ref _disposeStarted) != 0 || !IsEnabled(level)) return;

        var mode = (ApplicationMode)Volatile.Read(ref _mode);
        if (mode == ApplicationMode.Run && level < LogLevel.Information) return;
        var line = FormatLine(category, level, message, exception);
        var entry = new LogEntry(
            Interlocked.Increment(ref _sequence),
            mode,
            level,
            line,
            Utf8.GetByteCount(line) + NewLineBytes);
        var priority = level >= LogLevel.Warning;
        var wake = priority;

        lock (_queueSync)
        {
            if (_disposeStarted != 0) return;
            if (priority)
            {
                if (_priorityQueue.Count == PriorityQueueLimit)
                    _priorityQueue.Dequeue();
                _priorityQueue.Enqueue(entry);
            }
            else
            {
                if (_normalQueue.Count == NormalQueueLimit)
                    RemoveOldestNormalEntry();
                _normalQueue.AddLast(entry);
                _queuedNormalBytes += entry.Bytes;
                wake |= _queuedNormalBytes >= FlushThresholdBytes;
            }
        }

        if (wake) WakeWriter();
    }

    private void RemoveOldestNormalEntry()
    {
        var candidate = _normalQueue.First;
        while (candidate is not null && candidate.Value.Level != LogLevel.Debug)
            candidate = candidate.Next;
        candidate ??= _normalQueue.First;
        if (candidate is null) return;
        _queuedNormalBytes -= candidate.Value.Bytes;
        _normalQueue.Remove(candidate);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            while (true)
            {
                try
                {
                    await _wakeWriter.WaitAsync(FlushInterval).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                var entries = DrainQueues();
                if (entries.Count > 0)
                    TryWrite(entries);

                if (Volatile.Read(ref _disposeStarted) != 0 && QueuesAreEmpty())
                    return;
            }
        }
        catch
        {
            // A broken log directory cannot be allowed to terminate the process.
        }
        finally
        {
            _runFiles.Close();
            _debugFiles.Close();
        }
    }

    private List<LogEntry> DrainQueues()
    {
        lock (_queueSync)
        {
            var entries = new List<LogEntry>(_priorityQueue.Count + _normalQueue.Count);
            while (_priorityQueue.TryDequeue(out var priority))
                entries.Add(priority);
            for (var node = _normalQueue.First; node is not null; node = node.Next)
                entries.Add(node.Value);
            _normalQueue.Clear();
            _queuedNormalBytes = 0;
            entries.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
            return entries;
        }
    }

    private bool QueuesAreEmpty()
    {
        lock (_queueSync)
            return _priorityQueue.Count == 0 && _normalQueue.Count == 0;
    }

    private void TryWrite(IReadOnlyList<LogEntry> entries)
    {
        try
        {
            foreach (var group in entries.GroupBy(entry => entry.Mode))
            {
                var files = group.Key == ApplicationMode.Debug ? _debugFiles : _runFiles;
                files.Write(group.Select(entry => entry.Line));
            }
        }
        catch
        {
            _runFiles.Close();
            _debugFiles.Close();
        }
    }

    private void TryInitializeFiles(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            _runFiles.Initialize(Path.Combine(root, "vrc-fisher.log"));
            _debugFiles.Initialize(legacyPath: null);
        }
        catch
        {
            // The provider remains usable and retries when the first batch is written.
        }
    }

    private void WakeWriter()
    {
        try
        {
            if (_wakeWriter.CurrentCount == 0)
                _wakeWriter.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private static string FormatLine(
        string category,
        LogLevel level,
        string message,
        Exception? exception)
    {
        var safeMessage = SingleLine(message);
        var safeCategory = SingleLine(category);
        var exceptionText = exception is null ? null : SingleLine(exception.ToString());
        var line = $"{DateTimeOffset.Now:O} {LevelName(level)} {safeCategory} {safeMessage}";
        if (exceptionText is not null)
            line += $" exception=\"{exceptionText}\"";
        return TruncateUtf8(
            line,
            exception is null ? NormalLineLimitBytes : ExceptionLineLimitBytes);
    }

    private static string SingleLine(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string TruncateUtf8(string value, int limitBytes)
    {
        if (Utf8.GetByteCount(value) <= limitBytes) return value;
        const string marker = " truncated=true";
        var available = limitBytes - Utf8.GetByteCount(marker);
        var low = 0;
        var high = value.Length;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if (Utf8.GetByteCount(value.AsSpan(0, middle)) <= available)
                low = middle;
            else
                high = middle - 1;
        }
        if (low > 0 && char.IsHighSurrogate(value[low - 1])) low--;
        return value[..low] + marker;
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "UNK"
    };

    private static int NormalizeMode(ApplicationMode mode) =>
        mode == ApplicationMode.Debug ? (int)ApplicationMode.Debug : (int)ApplicationMode.Run;

    private readonly record struct LogEntry(
        long Sequence,
        ApplicationMode Mode,
        LogLevel Level,
        string Line,
        int Bytes);

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                provider.Enqueue(category, logLevel, formatter(state, exception), exception);
            }
            catch
            {
                // Logging failures are isolated from capture, inference, and input code.
            }
        }
    }

    private sealed class ModeFiles
    {
        private readonly string _directory;
        private readonly string _currentPath;
        private readonly string _historyPath;
        private readonly long _limitBytes;
        private StreamWriter? _writer;

        public ModeFiles(string directory, long limitBytes)
        {
            _directory = directory;
            _currentPath = Path.Combine(directory, "current.log");
            _historyPath = Path.Combine(directory, "history.log");
            _limitBytes = limitBytes;
        }

        public void Initialize(string? legacyPath)
        {
            Directory.CreateDirectory(_directory);
            DeleteIfPresent(_currentPath + ".tmp");
            DeleteIfPresent(_historyPath + ".tmp");

            var sources = new List<string>(2);
            if (legacyPath is not null && File.Exists(legacyPath) && new FileInfo(legacyPath).Length > 0)
                sources.Add(legacyPath);
            if (File.Exists(_currentPath) && new FileInfo(_currentPath).Length > 0)
                sources.Add(_currentPath);

            if (sources.Count > 0)
                ReplaceWithCombinedTail(_historyPath, sources, _limitBytes);
            else
                TrimExistingFile(_historyPath, _limitBytes);

            DeleteIfPresent(_currentPath);
            if (legacyPath is not null)
                DeleteIfPresent(legacyPath);
        }

        public void Write(IEnumerable<string> lines)
        {
            Directory.CreateDirectory(_directory);
            _writer ??= OpenWriter(_currentPath);
            foreach (var line in lines)
                _writer.WriteLine(line);
            _writer.Flush();

            if (_writer.BaseStream.Length <= _limitBytes) return;
            Close();
            ReplaceWithCombinedTail(
                _currentPath,
                [_currentPath],
                (long)(_limitBytes * 0.75));
            _writer = OpenWriter(_currentPath);
        }

        public void Close()
        {
            if (_writer is null) return;
            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch
            {
            }
            finally
            {
                _writer = null;
            }
        }

        private static StreamWriter OpenWriter(string path) => new(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 16 * 1024),
            Utf8,
            16 * 1024)
        {
            AutoFlush = false
        };

        private static void TrimExistingFile(string path, long limitBytes)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= limitBytes) return;
            ReplaceWithCombinedTail(path, [path], limitBytes);
        }

        private static void ReplaceWithCombinedTail(
            string destination,
            IReadOnlyList<string> sources,
            long limitBytes)
        {
            var text = new StringBuilder();
            foreach (var source in sources)
                text.Append(ReadTail(source, Math.Max(limitBytes * 2, 16 * 1024)));
            var tail = KeepCompleteTailLines(text.ToString(), limitBytes);
            var temporary = destination + ".tmp";
            try
            {
                DeleteIfPresent(temporary);
                File.WriteAllText(temporary, tail, Utf8);
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                DeleteIfPresent(temporary);
            }
        }

        private static string ReadTail(string path, long maximumBytes)
        {
            if (!File.Exists(path)) return string.Empty;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var offset = Math.Max(0, stream.Length - maximumBytes);
            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true);
            if (offset > 0)
                _ = reader.ReadLine();
            return reader.ReadToEnd();
        }

        private static string KeepCompleteTailLines(string text, long limitBytes)
        {
            var lines = text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return string.Empty;

            var selected = new Stack<string>();
            long used = 0;
            for (var index = lines.Length - 1; index >= 0; index--)
            {
                var line = lines[index];
                var bytes = Utf8.GetByteCount(line) + NewLineBytes;
                if (bytes > limitBytes && selected.Count == 0)
                {
                    selected.Push(TruncateUtf8(line, checked((int)Math.Min(int.MaxValue, limitBytes - 1))));
                    break;
                }
                if (used + bytes > limitBytes) break;
                selected.Push(line);
                used += bytes;
            }
            return string.Join(Environment.NewLine, selected) + Environment.NewLine;
        }

        private static void DeleteIfPresent(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
