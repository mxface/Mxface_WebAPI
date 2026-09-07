using System.Diagnostics;

namespace MxfaceWebAPI.Middleware
{
    // One "AllTrans" line per HTTP request — method, path, caller IP, final status code, elapsed
    // time, and the ClientId APIAuthorizationFilterAttribute resolves (if the request got that
    // far). Logged in a finally block so the real outcome is captured even if something downstream
    // throws (GlobalExceptionHandler handles the exception itself; this still logs the result).
    // Routed to its own "transactions" log file by FileLogger.RequestAccessLogCategory — separate
    // from BiometricControllerBase's per-call DB audit row, this is a lightweight file-only log.
    public class RequestAccessLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestAccessLoggingMiddleware> _logger;

        public RequestAccessLoggingMiddleware(RequestDelegate next, ILogger<RequestAccessLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();

                // ClientId isn't included here — FileLogger already prefixes every line with
                // [ClientId=...] once APIAuthorizationFilterAttribute resolves it, this line included.
                _logger.LogInformation(
                    "{Method} {Path} from {RemoteIp} -> {StatusCode} in {ElapsedMs}ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Connection.RemoteIpAddress,
                    context.Response.StatusCode,
                    stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
