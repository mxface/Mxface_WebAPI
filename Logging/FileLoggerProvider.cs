using System.Collections.Concurrent;

namespace MxfaceWebAPI.Logging
{
    // Minimal custom ILoggerProvider that appends every log line to a single file for this
    // process's run — no external logging package needed. One file per run (timestamped at
    // startup, see Extensions/FileLoggingExtensions.cs) so a specific test session's logs are
    // easy to isolate and hand to a third party (e.g. the ABIS/master gRPC provider team).
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _filePath;
        private readonly object _writeLock = new();
        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

        public FileLoggerProvider(string filePath)
        {
            _filePath = filePath;
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public ILogger CreateLogger(string categoryName) =>
            _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _filePath, _writeLock));

        public void Dispose() => _loggers.Clear();
    }

    internal class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _filePath;
        private readonly object _writeLock;

        public FileLogger(string categoryName, string filePath, object writeLock)
        {
            _categoryName = categoryName;
            _filePath = filePath;
            _writeLock = writeLock;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_categoryName}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (_writeLock)
            {
                File.AppendAllText(_filePath, line + Environment.NewLine);
            }
        }
    }
}
