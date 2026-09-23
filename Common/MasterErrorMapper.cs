using Microsoft.AspNetCore.Http;

namespace MxfaceWebAPI.Common
{
    public record MasterErrorMapping(int HttpStatus, string Field, string Message);

    // Maps the master's raw "em" text to this API's documented REST error contract (condition ->
    // HTTP status, response field name, exact message text). The master's full ec code catalogue
    // isn't available to us yet (the reference PDF's Error Codes table didn't extract its numeric
    // column cleanly), so known conditions are matched by keyword against the master's own "em"
    // text; anything unrecognized is forwarded as a 400/"message" passthrough of that text rather
    // than guessed, unless it clearly reads as an internal/engine failure.
    public static class MasterErrorMapper
    {
        public static MasterErrorMapping Map(string? em)
        {
            var lower = (em ?? string.Empty).ToLowerInvariant();

            if (lower.Contains("duplicate") && lower.Contains("biometric"))
            {
                return new MasterErrorMapping(StatusCodes.Status400BadRequest, "message", "Duplicate Biometric");
            }

            if (lower.Contains("duplicate") && (lower.Contains("external") || lower.Contains("reference")))
            {
                return new MasterErrorMapping(StatusCodes.Status400BadRequest, "message", "Duplicate External Id");
            }

            if (lower.Contains("base-64") || lower.Contains("base64"))
            {
                return new MasterErrorMapping(StatusCodes.Status400BadRequest, "message", "The input is not a valid Base-64 string.");
            }

            if (lower.Contains("format") || lower.Contains("resolution") || lower.Contains("invalid data") || lower.Contains("invalid biometric"))
            {
                return new MasterErrorMapping(StatusCodes.Status400BadRequest, "message", "Not valid biometric data or invalid format.");
            }

            if (lower.Contains("internal") || lower.Contains("engine"))
            {
                return new MasterErrorMapping(StatusCodes.Status500InternalServerError, "message", "Something went wrong, please try again later");
            }

            // "Citizen not found" (ec=-1020) — the referenceId doesn't exist under this client's
            // tenant (either never enrolled, or belongs to a different subscription key entirely).
            if (lower.Contains("citizen") && lower.Contains("not found"))
            {
                return new MasterErrorMapping(StatusCodes.Status400BadRequest, "message", "External Id Not found");
            }

            return new MasterErrorMapping(StatusCodes.Status400BadRequest, "message", em ?? string.Empty);
        }

        // Detects the master's "this referenceId already has an identity" rejection during
        // Enroll — e.g. Face was enrolled first for referenceId=101, then Finger is enrolled
        // later for the same referenceId. Callers use this to decide whether to retry via the
        // Update RPC instead of surfacing the rejection. ec="-2023" is the user's best-guess
        // code (not yet confirmed live) — kept alongside the em keyword match as a belt-and-
        // braces check; update/remove once the real code is observed.
        public static bool IsAlreadyEnrolledError(string? ec, string? em)
        {
            if (ec == "-2023")
            {
                return true;
            }

            return em != null && em.Contains("already exist", StringComparison.OrdinalIgnoreCase);
        }
    }
}
