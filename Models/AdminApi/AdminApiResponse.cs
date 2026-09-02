namespace MxfaceWebAPI.Models.AdminApi
{
    // Common envelope every Admin API call returns internally, so callers handle
    // success/failure/raw-content uniformly regardless of endpoint. Not part of the public REST
    // contract — internal to AbisAdminApiClient.
    public sealed class AdminApiResponse<T>
    {
        public bool IsSuccess { get; init; }
        public int StatusCode { get; init; }
        public T? Data { get; init; }
        public string? ErrorMessage { get; init; }
        public string? RawContent { get; init; }

        public static AdminApiResponse<T> Success(int statusCode, T? data, string? rawContent) => new()
        {
            IsSuccess = true,
            StatusCode = statusCode,
            Data = data,
            RawContent = rawContent
        };

        public static AdminApiResponse<T> Failure(int statusCode, string errorMessage, string? rawContent = null) => new()
        {
            IsSuccess = false,
            StatusCode = statusCode,
            ErrorMessage = errorMessage,
            RawContent = rawContent
        };
    }
}
