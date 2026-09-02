using System;
using System.Text.Json;
using Asp.Versioning;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MxfaceWebAPI.Common;
using MxfaceWebAPI.Filters;
using MxfaceWebAPI.Grpc.AbisClient;
using MxfaceWebAPI.Models;
using MxfaceWebAPI.Models.Response;
using MxfaceWebAPI.Models.Request.Face;

namespace MxfaceWebAPI.Controllers.V3
{
    [AllowAnonymous]
    [ApiVersion("3.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class FaceController : ControllerBase
    {
        // TODO: confirm the real "mode" value the master uses for a quality-only Match call.
        private const int QualityCheckMode = 0;

        private readonly ClientApiService.ClientApiServiceClient _clientApiClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FaceController> _logger;

        #region For Refrence Call
        public FaceController(ClientApiService.ClientApiServiceClient clientApiClient, IConfiguration configuration, ILogger<FaceController> logger)
        {
            _clientApiClient = clientApiClient;
            _configuration = configuration;
            _logger = logger;
        }
        #endregion


        #region For QualityAPI Call

        [MapToApiVersion("3.0")]
        [HttpPost("Quality", Name = "Quality")]
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [APIAuthorizationFilter]
        public async Task<QualityResponse> Quality([FromBody] QualityRequest request)
        {
            var response = new QualityResponse();
            try
            {
                // TODO: enc=0 (plaintext) until session-key/HMAC signing is implemented — see
                // abis_client.proto. skey/ci/hmac are only meaningful on the encrypted path.
                var grpcRequest = new ClientApiRequest
                {
                    SubscriptionKey = _configuration["GrpcServices:SubscriptionKey"] ?? string.Empty,
                    ReqId = Guid.NewGuid().ToString(),
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                    Enc = 0,
                    Mode = QualityCheckMode,
                    Data = JsonSerializer.Serialize(request)
                };

                var grpcResponse = await _clientApiClient.MatchAsync(grpcRequest);

                response.Result = JsonSerializer.Deserialize<JsonElement>(grpcResponse.ResponseJson);
                response.Code = 200;
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC Match (Quality) call failed");
                response.Code = 500;
                response.ErrorMessage = "Face quality service is unavailable.";
            }
            return response;
        }
        #endregion

        /// <summary>
        /// get Analytics (face rectangle, eyes location, mouth location)
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost("analytics", Name = "analytics")]
        [ApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [APIAuthorizationFilter]
        public async Task<ActionResult<FaceAnalyticsResponse>> Analytics([FromBody] DetectFace model)
        {
            FaceAnalyticsResponse faceAnalyticsResponse = new FaceAnalyticsResponse
            {
                ErrorCode = BiometricResponseCode.NotImplemented,
                ErrorMessage = "Face analytics is not yet available; the ABIS master gRPC support for it is under development."
            };
            return await ReturnResponse(faceAnalyticsResponse).ConfigureAwait(true);
        }

        #region Face API Emotion

        /// <summary>
        /// get Analytics (face rectangle, eyes location, mouth location)
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost("Emotion", Name = "Emotion")]
        [ApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [APIAuthorizationFilter]
        public async Task<ActionResult<FaceAnalyticsResponse>> Emotion([FromBody] DetectFace model)
        {
            FaceAnalyticsResponse faceAnalyticsResponse = new FaceAnalyticsResponse
            {
                ErrorCode = BiometricResponseCode.NotImplemented,
                ErrorMessage = "Face emotion detection is not yet available; the ABIS master gRPC support for it is under development."
            };

            return await ReturnResponse(faceAnalyticsResponse).ConfigureAwait(true);
        }
        #endregion Face API Emotion

        #region Face API Detect


        /// <summary>
        /// detect face in image
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [MapToApiVersion("3.0")]
        [HttpPost("detect", Name = "detect")]
        [APIAuthorizationFilter]
        public async Task<ActionResult<FaceDetectResponse>> Detect([FromBody] DetectFace model)
        {
            FaceDetectResponse faceDetectResponse = new FaceDetectResponse();

            // FaceDetectResponse doesn't inherit BiomatricBaseResponse (no ErrorCode field), so
            // ReturnResponse can't key off it here — return the not-implemented status directly.
            return await Task.FromResult<ActionResult<FaceDetectResponse>>(
                StatusCode(BiometricResponseCode.NotImplemented, faceDetectResponse)).ConfigureAwait(true);
        }
        #endregion

        // Adapted from BiometricControllerBase.ReturnResponse — duplicated rather than shared
        // since this controller inherits ControllerBase directly, not BiometricControllerBase
        // (changing that now would touch the already-working Quality action).
        private Task<ActionResult<T>> ReturnResponse<T>(T result)
        {
            if (result is BiomatricBaseResponse baseResponse && baseResponse.ErrorCode.HasValue)
            {
                return Task.FromResult<ActionResult<T>>(StatusCode(baseResponse.ErrorCode.Value, result));
            }

            return Task.FromResult<ActionResult<T>>(Ok(result));
        }
    }
}
