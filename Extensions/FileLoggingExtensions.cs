using Microsoft.AspNetCore.Http;
using MxfaceWebAPI.Logging;

namespace MxfaceWebAPI.Extensions
{
    public static class FileLoggingExtensions
    {
        // One set of log files per date, under a per-date folder, rotating to a zip past 5MB —
        // every process restart on the same day appends into the same files instead of creating
        // a new set, so active same-day dev/testing doesn't scatter across many small files.
        // Base directory is configurable via FileLogging:Directory; blank or unusable falls back
        // to <content root>/WebAPILogs.
        public static ILoggingBuilder AddMxfaceFileLogger(this ILoggingBuilder logging, IConfiguration configuration, string contentRootPath)
        {
            var baseDirectory = ResolveBaseDirectory(configuration["FileLogging:Directory"], contentRootPath);

            var today = DateTime.Now;
            var dateFolder = Path.Combine(baseDirectory, today.ToString("yyyy-MM-dd"));
            var dateStem = $"webapi-{today:yyyyMMdd}";

            // HttpContextAccessor's ambient-context storage is a static AsyncLocal shared across
            // every instance, so building this here (before the DI container exists) still
            // correctly reflects each real request's HttpContext later — lets the file logger tag
            // every line with the caller's ClientId once APIAuthorizationFilterAttribute resolves it,
            // not just the one dedicated request-access-log line.
            var httpContextAccessor = new HttpContextAccessor();

            logging.AddProvider(new FileLoggerProvider(dateFolder, dateStem, httpContextAccessor));

            return logging;
        }

        private static string ResolveBaseDirectory(string? configuredDirectory, string contentRootPath)
        {
            if (string.IsNullOrWhiteSpace(configuredDirectory))
            {
                return Path.Combine(contentRootPath, "WebAPILogs");
            }

            try
            {
                Directory.CreateDirectory(configuredDirectory);
                return configuredDirectory;
            }
            catch
            {
                return Path.Combine(contentRootPath, "WebAPILogs");
            }
        }
    }
}
