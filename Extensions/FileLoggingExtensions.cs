using MxfaceWebAPI.Logging;

namespace MxfaceWebAPI.Extensions
{
    public static class FileLoggingExtensions
    {
        // One log file per process run, under <content root>/logs, timestamped at startup —
        // makes it easy to grab exactly the file from one test session (e.g. to share with a
        // third-party API provider team) without trimming a continuously-growing shared log.
        public static ILoggingBuilder AddMxfaceFileLogger(this ILoggingBuilder logging, string contentRootPath)
        {
            var logsDirectory = Path.Combine(contentRootPath, "logs");
            var fileName = $"app-{DateTime.Now:yyyyMMdd-HHmmss}.log";
            var filePath = Path.Combine(logsDirectory, fileName);

            logging.AddProvider(new FileLoggerProvider(filePath));

            return logging;
        }
    }
}
