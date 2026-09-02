using Microsoft.AspNetCore.Diagnostics;
using MxfaceWebAPI.Models;
using MxfaceWebAPI.Services;
using Npgsql;
using System.Text.RegularExpressions;

namespace MxfaceWebAPI.Common
{
    // Normalizes any exception that escapes the MVC pipeline (e.g. a Postgres failure inside
    // APIAuthorizationFilterAttribute, which runs before an action's own try/catch) into the same
    // ApiErrorResponse shape the auth filter already returns for its own 401s, instead of the
    // framework's default ProblemDetails shape.
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
        {
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

            try
            {
                var emailService = httpContext.RequestServices.GetRequiredService<IEmailService>();
                await emailService.ExceptionMailSend($"{httpContext.Request.Method} {httpContext.Request.Path}", exception);
            }
            catch (Exception emailEx)
            {
                _logger.LogError(emailEx, "Failed to send exception alert email.");
            }

            var (statusCode, message) = ResolveClientResponse(exception);

            httpContext.Response.StatusCode = statusCode;
            await httpContext.Response.WriteAsJsonAsync(new ApiErrorResponse
            {
                Code = statusCode,
                Error = message
            }, cancellationToken);

            return true;
        }

        // Translates known Postgres constraint failures into a status code and message the caller
        // can actually act on (e.g. "this value already exists" instead of a generic 500).
        // Anything not recognised falls back to the generic message. No EF Core is used in this
        // project, so a constraint violation surfaces directly as PostgresException — no
        // DbUpdateException wrapper to unwrap first.
        private static (int StatusCode, string Message) ResolveClientResponse(Exception ex)
        {
            if (ex is PostgresException pgEx)
            {
                var column = ExtractKeyColumn(pgEx.Detail) ?? pgEx.ColumnName;

                return pgEx.SqlState switch
                {
                    PostgresErrorCodes.UniqueViolation =>
                        (BiometricResponseCode.Conflict, $"A record with this {column ?? "value"} already exists."),
                    PostgresErrorCodes.NotNullViolation =>
                        (BiometricResponseCode.BadRequest, $"'{column ?? "A required field"}' is required."),
                    PostgresErrorCodes.ForeignKeyViolation =>
                        (BiometricResponseCode.BadRequest, "This record references something that does not exist."),
                    PostgresErrorCodes.CheckViolation =>
                        (BiometricResponseCode.BadRequest, "The submitted data does not meet a required rule."),
                    PostgresErrorCodes.StringDataRightTruncation =>
                        (BiometricResponseCode.BadRequest, "One of the submitted values is too long."),
                    _ => (BiometricResponseCode.ServiceUnavailable, "An unexpected error occurred. Please try again later.")
                };
            }

            return (BiometricResponseCode.ServiceUnavailable, "An unexpected error occurred. Please try again later.");
        }

        // Postgres includes the offending column in the constraint-violation detail message,
        // e.g. "Key (clientcode)=(MANTRA) already exists." — pull it out so the client-facing
        // message can name the actual field instead of saying "value".
        private static string? ExtractKeyColumn(string? detail)
        {
            if (string.IsNullOrEmpty(detail))
            {
                return null;
            }

            var match = Regex.Match(detail, @"^Key \(([^)]+)\)=");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
