namespace MxfaceWebAPI.Models.Response.Group
{
    // Matches the documented Group API v3 error envelope exactly — errorMessage/error and
    // statusCode/code are each duplicated pairs, both equal to the HTTP status returned. This is
    // a different shape from ApiErrorResponse (used by Finger/Iris/Face) — scoped to Group only,
    // per the published Group API v3 contract.
    public class GroupErrorResponse
    {
        public string ErrorMessage { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public int Code { get; set; }
        public string Error { get; set; } = string.Empty;

        public static GroupErrorResponse Create(int statusCode, string message) => new()
        {
            ErrorMessage = message,
            StatusCode = statusCode,
            Code = statusCode,
            Error = message
        };
    }
}
