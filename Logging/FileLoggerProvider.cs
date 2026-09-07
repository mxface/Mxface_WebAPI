using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace MxfaceWebAPI.Logging
{
    // Custom ILoggerProvider that appends every log line to one of 4 files per date
    // (debug/success/error/transactions — see FileLogBucket), grouped under a per-date folder,
    // rotating (zip + fresh file) if any one bucket's file grows past 5MB. Every process restart
    // on the same day appends into the same set of files (see Extensions/FileLoggingExtensions.cs)
    // instead of starting a new set, so active same-day dev/testing doesn't scatter across many
    // small files.
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _dateFolder;
        private readonly string _dateStem;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly FileLogState _state = new();
        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

        public FileLoggerProvider(string dateFolder, string dateStem, IHttpContextAccessor httpContextAccessor)
        {
            _dateFolder = dateFolder;
            _dateStem = dateStem;
            _httpContextAccessor = httpContextAccessor;
            Directory.CreateDirectory(_dateFolder);
        }

        public ILogger CreateLogger(string categoryName) =>
            _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _dateFolder, _dateStem, _httpContextAccessor, _state));

        public void Dispose() => _loggers.Clear();
    }

    // Which of the 4 per-date files a log line lands in.
    internal enum FileLogBucket
    {
        Debug,
        Success,
        Error,
        Transactions
    }

    // Shared across every category's FileLogger for one process, since they all write to the same
    // set of physical files and must agree on the write lock.
    internal sealed class FileLogState
    {
        public readonly object WriteLock = new();
    }

    internal class FileLogger : ILogger
    {
        private const long MaxLogFileSizeBytes = 5 * 1024 * 1024; // 5 MB

        // Dedicated category used by RequestAccessLoggingMiddleware — every line from it goes to
        // the "transactions" bucket regardless of level (it's the per-HTTP-request "AllTrans" log).
        public const string RequestAccessLogCategory = "MxfaceWebAPI.Middleware.RequestAccessLoggingMiddleware";

        private readonly string _categoryName;
        private readonly string _dateFolder;
        private readonly string _dateStem;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly FileLogState _state;

        public FileLogger(string categoryName, string dateFolder, string dateStem, IHttpContextAccessor httpContextAccessor, FileLogState state)
        {
            _categoryName = categoryName;
            _dateFolder = dateFolder;
            _dateStem = dateStem;
            _httpContextAccessor = httpContextAccessor;
            _state = state;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var clientId = _httpContextAccessor.HttpContext?.Items.TryGetValue("ClientId", out var value) == true ? value : null;
            var clientTag = clientId is not null ? $"[ClientId={clientId}] " : string.Empty;

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_categoryName}: {clientTag}{formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            var bucket = ResolveBucket(logLevel);
            var bucketSuffix = bucket.ToString().ToLowerInvariant();
            var currentLogPath = Path.Combine(_dateFolder, $"{_dateStem}-{bucketSuffix}.log");

            lock (_state.WriteLock)
            {
                if (File.Exists(currentLogPath) && new FileInfo(currentLogPath).Length >= MaxLogFileSizeBytes)
                {
                    RotateToZip(bucketSuffix, currentLogPath);
                }

                File.AppendAllText(currentLogPath, line + Environment.NewLine);
            }
        }

        private FileLogBucket ResolveBucket(LogLevel logLevel)
        {
            if (_categoryName == RequestAccessLogCategory)
            {
                return FileLogBucket.Transactions;
            }

            return logLevel switch
            {
                LogLevel.Trace or LogLevel.Debug => FileLogBucket.Debug,
                LogLevel.Warning or LogLevel.Error or LogLevel.Critical => FileLogBucket.Error,
                _ => FileLogBucket.Success
            };
        }

        // Called under _state.WriteLock — zips the current file's full content out, then deletes
        // it so this bucket's stem name is free again for a fresh file to keep growing. The next
        // zip number is worked out by scanning disk rather than an in-memory counter, since
        // multiple process restarts on the same day share this same file set — an in-memory
        // counter would restart at 1 every run and silently overwrite an earlier run's zip.
        private void RotateToZip(string bucketSuffix, string currentLogPath)
        {
            var nextNumber = NextRotationNumber(bucketSuffix);
            var zipPath = Path.Combine(_dateFolder, $"{_dateStem}-{bucketSuffix}-{nextNumber}.zip");

            using (var zipStream = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(currentLogPath, Path.GetFileName(currentLogPath), CompressionLevel.Optimal);
            }

            File.Delete(currentLogPath);
        }

        private int NextRotationNumber(string bucketSuffix)
        {
            var pattern = new Regex($@"^{Regex.Escape(_dateStem)}-{Regex.Escape(bucketSuffix)}-(\d+)\.zip$");
            var maxExisting = 0;

            foreach (var file in Directory.EnumerateFiles(_dateFolder, $"{_dateStem}-{bucketSuffix}-*.zip"))
            {
                var match = pattern.Match(Path.GetFileName(file));
                if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > maxExisting)
                {
                    maxExisting = number;
                }
            }

            return maxExisting + 1;
        }
    }
}
