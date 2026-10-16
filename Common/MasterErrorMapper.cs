using Microsoft.AspNetCore.Http;

namespace MxfaceWebAPI.Common
{
    public record MasterErrorMapping(int HttpStatus, string Field, string Message);

    // Maps the master's raw "em" text to this API's documented REST error contract (condition ->
    // HTTP status, response field name, exact message text). The master's full ec code catalogue
    // isn't available to us yet (the reference PDF's Error Codes table didn't extract its numeric
    // column cleanly), so known conditions are matched by keyword against the master's own "em"
    // text, plus a few ec codes confirmed live (see Map(ec, em, ...)); anything unrecognized is forwarded as a 400/"message" passthrough of that text rather
    // than guessed, unless it clearly reads as an internal/engine failure.
    public static class MasterErrorMapper
    {
        // Mapped message for the master's "Citizen not found" — public so callers (e.g. Face
        // Delete) can tell "referenceId doesn't exist in ABIS" apart from other failures.
        public const string ExternalIdNotFoundMessage = "External Id Not found";

        private static readonly MasterErrorMapping InvalidBiometric =
            new(StatusCodes.Status400BadRequest, "message", "Not valid biometric data or invalid format.");

        // ec codes the master returns when it can't extract a template from the submitted image —
        // e.g. a fingerprint image sent to an Iris endpoint. Confirmed live: Iris Verify ->
        // -8015 "Template extraction failed", Iris Enroll -> 22 "Error 22". The ABIS v2.0 PDF's
        // examples also show -2003 "Template extraction failed (pipeline)" and ec 22 for the same case.
        private static readonly HashSet<string> TemplateExtractionErrorCodes = new() { "-8015", "-2003", "22" };

        // The master's generic "Internal Server Error" code. Identify (Search) returns this instead
        // of a template-extraction code when the probe is the wrong modality (confirmed live for
        // Iris Search with a fingerprint image).
        private const string InternalServerErrorCode = "-1000";

        // internalErrorMeansInvalidBiometric: set only by callers that have observed the master
        // returning -1000 for bad probe data (see InternalServerErrorCode). A genuine master fault
        // on those calls will also surface as "invalid biometric", so keep this opt-in.
        public static MasterErrorMapping Map(string? ec, string? em, bool internalErrorMeansInvalidBiometric = false)
        {
            if (ec != null && TemplateExtractionErrorCodes.Contains(ec))
            {
                return InvalidBiometric;
            }

            if (internalErrorMeansInvalidBiometric && ec == InternalServerErrorCode)
            {
                return InvalidBiometric;
            }

            return Map(em);
        }

        public static MasterErrorMapping Map(string? em)
        {
            var lower = (em ?? string.Empty).ToLowerInvariant();

            if (lower.Contains("template extraction") || lower.Contains("unable to process biometric"))
            {
                return InvalidBiometric;
            }

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
                return InvalidBiometric;
            }

            if (lower.Contains("internal") || lower.Contains("engine"))
            {
                return new MasterErrorMapping(StatusCodes.Status500InternalServerError, "message", "Something went wrong, please try again later");
            }

            // "Citizen not found" (ec=-1020) — the referenceId doesn't exist under this client's
            // tenant (either never enrolled, or belongs to a different subscription key entirely).
            if (lower.Contains("citizen") && lower.Contains("not found"))
            {
                return new MasterErrorMapping(StatusCodes.Status400BadRequest, "message", ExternalIdNotFoundMessage);
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
