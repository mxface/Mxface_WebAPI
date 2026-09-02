using Microsoft.AspNetCore.Http;

namespace MxfaceWebAPI.Common
{
    // Single source of truth for the status codes biometric controllers write onto
    // BiomatricBaseResponse.Code / Response.StatusCode, so no action hardcodes a magic number.
    public static class BiometricResponseCode
    {
        public const int Success = StatusCodes.Status200OK;
        public const int BadRequest = StatusCodes.Status400BadRequest;
        public const int Conflict = StatusCodes.Status409Conflict;
        public const int ServiceUnavailable = StatusCodes.Status500InternalServerError;
        public const int NotImplemented = StatusCodes.Status501NotImplemented;
    }
}
