namespace MxfaceWebAPI.Models.Response
{
    // Same duplicated-pair error envelope as Group API v3's GroupErrorResponse (errorMessage/error
    // and statusCode/code, both equal to the HTTP status) — reused here per an explicit ask to
    // match that shape for FaceController error responses.
    public class ApiErrorEnvelope
    {
        public string ErrorMessage { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public int Code { get; set; }
        public string Error { get; set; } = string.Empty;

        public static ApiErrorEnvelope Create(int statusCode, string message) => new()
        {
            ErrorMessage = message,
            StatusCode = statusCode,
            Code = statusCode,
            Error = message
        };
    }
}
