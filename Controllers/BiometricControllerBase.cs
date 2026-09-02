using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using MxfaceWebAPI.Common;
using MxfaceWebAPI.Data;
using MxfaceWebAPI.Grpc;
using MxfaceWebAPI.Grpc.AbisClient;
using MxfaceWebAPI.Models;

namespace MxfaceWebAPI.Controllers
{
    // Shared call path for biometric controllers: build the envelope, invoke the given RPC,
    // deserialize the response, translate a failed call into a consistent error shape, and log
    // every call (success or failure) into faceclient_db.transactions — so each action only
    // supplies its mode, payload, which RPC to call, and (once wired) its feature type.
    public abstract class BiometricControllerBase : ControllerBase
    {
        private const int DefaultCallTimeoutSeconds = 10;

        // TODO: no real quota-consumption rule confirmed yet — placeholder, same class of
        // unconfirmed value as Mode/Version elsewhere in this project.
        private const int DefaultQuotaCount = 1;

        // faceclient_db.transactions column limits (character_maximum_length), verified live —
        // error/errorpoint are varchar(10), meant for short codes, not full exception messages.
        // The full detail is never lost: it's already captured by the _logger.LogError calls above.
        private const int ReqIdMaxLength = 36;
        private const int ErrorMaxLength = 10;
        private const int ClientIpMaxLength = 50;

        // The master's "data" sub-object uses camelCase field names (e.g. "referenceId") that
        // don't match our PascalCase response model properties by name alone.
        private static readonly JsonSerializerOptions ResponseDeserializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IClientApiEnvelopeFactory _envelopeFactory;
        private readonly IConfiguration _configuration;
        private readonly IPostgresHelper _postgresHelper;
        protected readonly ILogger _logger;

        protected BiometricControllerBase(
            IClientApiEnvelopeFactory envelopeFactory,
            IConfiguration configuration,
            IPostgresHelper postgresHelper,
            ILogger logger)
        {
            _envelopeFactory = envelopeFactory;
            _configuration = configuration;
            _postgresHelper = postgresHelper;
            _logger = logger;
        }

        // Resolved once per request by APIAuthorizationFilterAttribute (see Filters/) and stashed
        // in HttpContext.Items — avoids a second Postgres round-trip for controllers that also need it.
        protected long? ResolvedClientId =>
            HttpContext.Items.TryGetValue("ClientId", out var value) && value is long clientId ? clientId : null;

        // The caller's own subscription key, resolved by the same auth filter — forwarded to the
        // master as-is so it can identify/validate/log/bill under this specific client's identity.
        protected string? ResolvedSubscriptionKey =>
            HttpContext.Items.TryGetValue("SubscriptionKey", out var value) && value is string key ? key : null;

        // Wraps a response as 200 OK, or as its own ErrorCode-specified status when
        // BiomatricBaseResponse.ErrorCode was set to signal a failure (validation, or a
        // not-yet-implemented endpoint). Not constrained to BiomatricBaseResponse — a response
        // type that doesn't inherit it (e.g. FaceInfo) just always returns Ok.
        protected Task<ActionResult<T>> ReturnResponse<T>(T result)
        {
            if (result is BiomatricBaseResponse baseResponse && baseResponse.ErrorCode.HasValue)
            {
                return Task.FromResult<ActionResult<T>>(StatusCode(baseResponse.ErrorCode.Value, result));
            }

            return Task.FromResult<ActionResult<T>>(Ok(result));
        }

        protected async Task<TResponse> CallAsync<TResponse>(
            int mode,
            CommonRequest requestPayload,
            Func<ClientApiRequest, CallOptions, Task<ClientApiResponse>> grpcCall,
            string operationName,
            int featureType = 0,
            // Optional Enroll->Update fallback: when the master rejects Enroll because the
            // referenceId already exists under a different modality (e.g. Face enrolled first,
            // then Finger enrolled later for the same referenceId), callers can pass a predicate
            // over the raw (ec, em) plus a fallback RPC (Update) to retry with instead — the
            // failed Enroll attempt is still logged as its own transaction row, then the retry
            // reuses this same method (without fallback params, so it can't loop) for the Update
            // call. Both existing params keep their old behavior when these are omitted (null).
            Func<string?, string?, bool>? shouldRetryWithFallback = null,
            Func<ClientApiRequest, CallOptions, Task<ClientApiResponse>>? fallbackGrpcCall = null,
            int? fallbackMode = null)
            where TResponse : BiomatricBaseResponse, new()
        {
            var envelope = _envelopeFactory.Create(mode, requestPayload, ResolvedSubscriptionKey ?? string.Empty);
            var requestTimestamp = DateTime.UtcNow;
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            var clientId = ResolvedClientId ?? 0;

            try
            {
                var timeoutSeconds = _configuration.GetValue("GrpcServices:CallTimeoutSeconds", DefaultCallTimeoutSeconds);
                var callOptions = new CallOptions(
                    deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds),
                    cancellationToken: HttpContext.RequestAborted);

                var grpcResponse = await grpcCall(envelope, callOptions);
                var responseTimestamp = DateTime.UtcNow;

                // The gRPC transport call succeeding (no RpcException) is NOT the same as the
                // master's business operation succeeding. The master's envelope is
                // {ec, em, ts, ver, reqId, data:{...actual result...}} — "em" is populated on
                // EVERY response, including success ("em":"Success"), so only "ec" != "0" means
                // the master actually rejected the call. The real payload lives inside "data".
                string? masterErrorCode = null;
                string? masterErrorMessage = null;
                JsonElement? dataElement = null;
                using (var masterResponseDoc = JsonDocument.Parse(grpcResponse.ResponseJson))
                {
                    if (masterResponseDoc.RootElement.TryGetProperty("em", out var emEl) && emEl.ValueKind == JsonValueKind.String)
                    {
                        masterErrorMessage = emEl.GetString();
                    }
                    if (masterResponseDoc.RootElement.TryGetProperty("ec", out var ecEl) && ecEl.ValueKind == JsonValueKind.String)
                    {
                        masterErrorCode = ecEl.GetString();
                    }
                    if (masterResponseDoc.RootElement.TryGetProperty("data", out var dataEl))
                    {
                        dataElement = dataEl.Clone();
                    }
                }

                var masterFailed = !string.IsNullOrEmpty(masterErrorCode) && masterErrorCode != "0";

                if (masterFailed)
                {
                    _logger.LogError("Master rejected {Operation}: ec={ErrorCode} em={ErrorMessage}", operationName, masterErrorCode, masterErrorMessage);

                    await LogTransactionSafeAsync(new TransactionLogEntry
                    {
                        ClientId = clientId,
                        ReqId = Truncate(envelope.ReqId, ReqIdMaxLength),
                        RequestPayload = envelope.Data,
                        RequestTimestamp = requestTimestamp,
                        ResponsePayload = grpcResponse.ResponseJson,
                        Error = Truncate(masterErrorCode ?? "MASTER", ErrorMaxLength),
                        ResponseTimestamp = responseTimestamp,
                        ErrorPoint = "MASTER",
                        FeatureType = featureType,
                        QuotaCount = DefaultQuotaCount,
                        ClientIp = Truncate(clientIp, ClientIpMaxLength),
                        CreatedBy = clientId
                    });

                    if (fallbackGrpcCall != null && (shouldRetryWithFallback?.Invoke(masterErrorCode, masterErrorMessage) ?? false))
                    {
                        _logger.LogInformation(
                            "{Operation}: master reported '{ErrorMessage}' — retrying via Update for an existing identity",
                            operationName, masterErrorMessage);
                        return await CallAsync<TResponse>(
                            fallbackMode ?? mode, requestPayload, fallbackGrpcCall, operationName + "->Update", featureType);
                    }

                    var mapping = MasterErrorMapper.Map(masterErrorMessage);
                    Response.StatusCode = mapping.HttpStatus;
                    var errorResponse = new TResponse { Code = mapping.HttpStatus };
                    if (mapping.Field == "message")
                    {
                        errorResponse.Message = mapping.Message;
                    }
                    else
                    {
                        errorResponse.ErrorMessage = mapping.Message;
                    }
                    return errorResponse;
                }

                // Not every operation's "data" is a JSON object on success — Delete's, for
                // example, comes back as an empty string rather than {}. Deserializing a
                // non-object value (string/number/array/null) into TResponse throws, so only
                // attempt it when "data" is actually an object; otherwise there's nothing to map
                // and a fresh TResponse is correct.
                var response = (dataElement.HasValue && dataElement.Value.ValueKind == JsonValueKind.Object)
                    ? JsonSerializer.Deserialize<TResponse>(dataElement.Value.GetRawText(), ResponseDeserializerOptions) ?? new TResponse()
                    : new TResponse();
                response.Code = BiometricResponseCode.Success;
                response.Message = "Success";

                // The master doesn't invent its own identity id on Enroll — it echoes back the
                // referenceId we sent in demographics.referenceId. Mapped explicitly here (rather
                // than via [JsonPropertyName] on the model) so this stays internal-only and never
                // renames IdentityId in our own REST output.
                if (response is BiometricEnrollResponse enrollResponse
                    && dataElement.HasValue
                    && dataElement.Value.TryGetProperty("referenceId", out var referenceIdEl)
                    && referenceIdEl.ValueKind == JsonValueKind.String)
                {
                    enrollResponse.IdentityId = referenceIdEl.GetString();
                }

                // Match/Verify's real result isn't a flat score — it's
                // data.candidates[0].analytics.confidence (a numeric string), nested inside a
                // candidate/modality/combination structure the generic deserialize can't reach.
                // Matched is derived from confidence (>0 = matched). A real comparison outcome
                // (match or no-match) is a normal 200 result either way, so the response is
                // trimmed down to ONLY {"matched": 0|1} — Code/Message/MatchingScore are
                // intentionally nulled here so they're omitted from the REST response. This is
                // distinct from a technical failure (validation, master rejection, template
                // extraction error), which still returns the full 400/500 + message/errorMessage
                // contract from the masterFailed branch above, untouched.
                if (response is VerifyFingerPrintsResponse verifyResponse)
                {
                    double confidence = 0;
                    if (dataElement.HasValue
                        && dataElement.Value.TryGetProperty("candidates", out var candidatesEl)
                        && candidatesEl.ValueKind == JsonValueKind.Array
                        && candidatesEl.GetArrayLength() > 0
                        && candidatesEl[0].TryGetProperty("analytics", out var analyticsEl)
                        && analyticsEl.TryGetProperty("confidence", out var confidenceEl)
                        && confidenceEl.ValueKind == JsonValueKind.String
                        && double.TryParse(confidenceEl.GetString(), out var parsedConfidence))
                    {
                        confidence = parsedConfidence;
                    }

                    verifyResponse.Matched = confidence > 0 ? 1 : 0;
                    verifyResponse.MatchingScore = null;
                    verifyResponse.Code = null;
                    verifyResponse.Message = null;
                }

                // Same trimming as FingerPrint Verify above, for Iris's separate response type.
                // Shape is assumed identical (data.candidates[0].analytics.confidence) — unconfirmed
                // for Iris specifically since no real Iris Match success has been captured yet.
                if (response is VerifyIrisResponse irisVerifyResponse)
                {
                    double irisConfidence = 0;
                    if (dataElement.HasValue
                        && dataElement.Value.TryGetProperty("candidates", out var irisCandidatesEl)
                        && irisCandidatesEl.ValueKind == JsonValueKind.Array
                        && irisCandidatesEl.GetArrayLength() > 0
                        && irisCandidatesEl[0].TryGetProperty("analytics", out var irisAnalyticsEl)
                        && irisAnalyticsEl.TryGetProperty("confidence", out var irisConfidenceEl)
                        && irisConfidenceEl.ValueKind == JsonValueKind.String
                        && double.TryParse(irisConfidenceEl.GetString(), out var irisParsedConfidence))
                    {
                        irisConfidence = irisParsedConfidence;
                    }

                    irisVerifyResponse.Matched = irisConfidence > 0 ? 1 : 0;
                    irisVerifyResponse.MatchingScore = null;
                    irisVerifyResponse.Code = null;
                    irisVerifyResponse.Message = null;
                }

                // Search/Identify's real result is a candidate LIST (1:N), unlike Verify's
                // single 1:1 comparison — data.candidates[].referenceId +
                // data.candidates[].analytics.confidence map to each MatchResult entry. A search
                // with zero candidates is still a normal 200 result (empty MatchResult), same
                // reasoning as Verify's no-match case. Code/Message are nulled so only
                // {"matchResult":[...]} is returned on success.
                if (response is BiometricSearchResponse searchResponse)
                {
                    var matchResults = new List<MatchResultEntry>();
                    if (dataElement.HasValue
                        && dataElement.Value.TryGetProperty("candidates", out var searchCandidatesEl)
                        && searchCandidatesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var candidateEl in searchCandidatesEl.EnumerateArray())
                        {
                            string? externalId = candidateEl.TryGetProperty("referenceId", out var refIdEl)
                                && refIdEl.ValueKind == JsonValueKind.String
                                ? refIdEl.GetString()
                                : null;

                            float? matchingScore = null;
                            if (candidateEl.TryGetProperty("analytics", out var candidateAnalyticsEl)
                                && candidateAnalyticsEl.TryGetProperty("confidence", out var candidateConfidenceEl)
                                && candidateConfidenceEl.ValueKind == JsonValueKind.String
                                && float.TryParse(candidateConfidenceEl.GetString(), out var parsedScore))
                            {
                                matchingScore = parsedScore;
                            }

                            matchResults.Add(new MatchResultEntry { ExternalId = externalId, MatchingScore = matchingScore });
                        }
                    }

                    searchResponse.MatchResult = matchResults;
                    searchResponse.Code = null;
                    searchResponse.Message = null;
                }

                await LogTransactionSafeAsync(new TransactionLogEntry
                {
                    ClientId = clientId,
                    ReqId = Truncate(envelope.ReqId, ReqIdMaxLength),
                    RequestPayload = envelope.Data,
                    RequestTimestamp = requestTimestamp,
                    ResponsePayload = grpcResponse.ResponseJson,
                    Error = string.Empty,
                    ResponseTimestamp = responseTimestamp,
                    ErrorPoint = string.Empty,
                    FeatureType = featureType,
                    QuotaCount = DefaultQuotaCount,
                    ClientIp = Truncate(clientIp, ClientIpMaxLength),
                    CreatedBy = clientId
                });

                return response;
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC {Operation} call failed with status {StatusCode}", operationName, ex.StatusCode);
                Response.StatusCode = BiometricResponseCode.ServiceUnavailable;

                await LogTransactionSafeAsync(new TransactionLogEntry
                {
                    ClientId = clientId,
                    ReqId = Truncate(envelope.ReqId, ReqIdMaxLength),
                    RequestPayload = envelope.Data,
                    RequestTimestamp = requestTimestamp,
                    ResponsePayload = "{}",
                    Error = Truncate(ex.StatusCode.ToString(), ErrorMaxLength),
                    ResponseTimestamp = DateTime.UtcNow,
                    ErrorPoint = "GRPC",
                    FeatureType = featureType,
                    QuotaCount = DefaultQuotaCount,
                    ClientIp = Truncate(clientIp, ClientIpMaxLength),
                    CreatedBy = clientId
                });

                return new TResponse
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = $"{operationName} service is unavailable."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", operationName);
                Response.StatusCode = BiometricResponseCode.ServiceUnavailable;

                await LogTransactionSafeAsync(new TransactionLogEntry
                {
                    ClientId = clientId,
                    ReqId = Truncate(envelope.ReqId, ReqIdMaxLength),
                    RequestPayload = envelope.Data,
                    RequestTimestamp = requestTimestamp,
                    ResponsePayload = "{}",
                    Error = Truncate(ex.GetType().Name, ErrorMaxLength),
                    ResponseTimestamp = DateTime.UtcNow,
                    ErrorPoint = "UNEXPECTED",
                    FeatureType = featureType,
                    QuotaCount = DefaultQuotaCount,
                    ClientIp = Truncate(clientIp, ClientIpMaxLength),
                    CreatedBy = clientId
                });

                return new TResponse
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = $"{operationName} failed unexpectedly."
                };
            }
        }

        // A failed audit-log write must never turn a real (successful or already-failed)
        // biometric call into a different outcome for the caller — swallow and log only.
        private async Task LogTransactionSafeAsync(TransactionLogEntry entry)
        {
            try
            {
                await _postgresHelper.LogTransactionAsync(entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write transaction log for reqId {ReqId}", entry.ReqId);
            }
        }

        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];
    }
}
