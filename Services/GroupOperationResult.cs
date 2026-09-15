using MxfaceWebAPI.Common;
using MxfaceWebAPI.Models.Response.Group;

namespace MxfaceWebAPI.Services
{
    // Outcome of a Create/Update/Delete group operation — carries enough to let GroupController
    // map straight onto ApiErrorResponse/200 without needing its own status-code logic.
    public sealed class GroupOperationResult
    {
        public bool Success { get; init; }
        public int StatusCode { get; init; }
        public string? ErrorMessage { get; init; }
        public GroupResponse? Group { get; init; }

        public static GroupOperationResult Ok(GroupResponse? group = null) =>
            new() { Success = true, StatusCode = BiometricResponseCode.Success, Group = group };

        public static GroupOperationResult Fail(int statusCode, string message) =>
            new() { Success = false, StatusCode = statusCode, ErrorMessage = message };
    }
}
