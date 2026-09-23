using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MxfaceWebAPI.Common;
using MxfaceWebAPI.Data;
using MxfaceWebAPI.Filters;
using MxfaceWebAPI.Grpc;
using MxfaceWebAPI.Grpc.AbisClient;
using MxfaceWebAPI.Models;
using AnalyzeImage = MxfaceWebAPI.Grpc.Extract.AnalyzeImage;
using FeatureFlags = MxfaceWebAPI.Grpc.Extract.FeatureFlags;

namespace MxfaceWebAPI.Controllers
{
    // Subscription-key auth (via [APIAuthorizationFilter] on each action) gates these endpoints,
    // not the global JWT policy — AllowAnonymous opts out of that so the filter is what runs.
    [AllowAnonymous]
    [Route("api/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "FingerPrint API")]
    public class FingerPrintController : BiometricControllerBase
    {
        // TODO: confirm the real mode value the master uses for each FingerPrint operation — see
        // FaceController.QualityCheckMode for the same open question on the Face side.
        private const int VerifyMode = 0;
        private const int EnrollMode = 0;
        private const int SearchMode = 0;
        private const int DeleteMode = 0;
        private const int LivenessMode = 0;
        private const int FinagerModality = 1;

        // TODO: same unconfirmed-placeholder caveat as FaceController's BioFormat/BioPosition codes —
        // neither abis_client.proto nor extract.proto defines what these integers mean beyond
        // BioModality (see CLAUDE.md "Known placeholder/unconfirmed values").
        private const int FingerPosition = 0;
        private static int MapBioFormatCode(string format) => format switch
        {
            "BMP" => 0,
            "JPEG" => 1,
            "PNG" => 2,
            _ => throw new InvalidDataException($"Unsupported image format for BioAnalyze: {format}")
        };

        // Per the official ABIS Client API v2.0 reference doc's Biometric Data Formats table
        // (§21): format="BMP" pairs with version="0" — "2005"/"2011" only apply to the ISO
        // FIR/IIR/FID record formats, not raster formats like BMP/RAW/JPEG/PNG/WSQ.
        private const string BmpBioDataVersion = "0";

        // REST error-contract text for a missing/invalid required field, per the documented
        // condition -> HTTP code -> field -> message table.
        private const string InvalidRequestMessage = "Invalid request. Please pass valid json with all required parameters.";

        private readonly ClientApiService.ClientApiServiceClient _clientApiClient;

        #region For Refrence Call
        public FingerPrintController(
            ClientApiService.ClientApiServiceClient clientApiClient,
            IClientApiEnvelopeFactory envelopeFactory,
            IConfiguration configuration,
            IPostgresHelper postgresHelper,
            ILogger<FingerPrintController> logger)
            : base(envelopeFactory, configuration, postgresHelper, logger)
        {
            _clientApiClient = clientApiClient;
        }
        #endregion

        #region fingerprint Verify
        /// <summary>
        /// Compares two fingerprint images and reports whether they belong to the same person (1:1 match).
        /// </summary>
        /// <param name="request">The two fingerprint images to compare.</param>
        /// <returns>The matching score and whether the pair is considered a match.</returns>
        [ApiExplorerSettings(GroupName = "BiometricAPI")]
        [APIAuthorizationFilter]
        [Route("Verify", Name = "VerifyFingerPrint")]
        [HttpPost]
        public Task<VerifyFingerPrintsResponse> Verify([FromBody] VerifyFingerPrintsRequest request)
        {
            VerifyFingerPrintsResponse response = new VerifyFingerPrintsResponse();

            if (request == null || string.IsNullOrEmpty(request.FingerPrint1) || string.IsNullOrEmpty(request.FingerPrint2))
            {
                response.ErrorMessage = InvalidRequestMessage;
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return Task.FromResult(response);
            }

            try
            {
                var (width1, height1) = global::MxfaceWebAPI.CommonHelper.CommonHelper.GetBmpDimensions(request.FingerPrint1);
                var (width2, height2) = global::MxfaceWebAPI.CommonHelper.CommonHelper.GetBmpDimensions(request.FingerPrint2);
                var masterPayload = new FingerPrintVerifyMasterPayload
                {
                    Probe = new FingerprintProbeGalleryPayload
                    {
                        Fingerprints = new FingerprintsPayload
                        {
                            BioData = new List<BioData>
                            {
                                new BioData { Format = "BMP", Version = BmpBioDataVersion, Wd = width1, Ht = height1, Data = request.FingerPrint1 }
                            }
                        }
                    },
                    Gallery = new FingerprintProbeGalleryPayload
                    {
                        Fingerprints = new FingerprintsPayload
                        {
                            BioData = new List<BioData>
                            {
                                new BioData { Format = "BMP", Version = BmpBioDataVersion, Wd = width2, Ht = height2, Data = request.FingerPrint2 }
                            }
                        }
                    }
                };

                return CallAsync<VerifyFingerPrintsResponse>(
                    VerifyMode, masterPayload, (r, o) => _clientApiClient.MatchAsync(r, o).ResponseAsync, nameof(Verify),
                    BiometricFeatureType.FingerPrintVerify);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "{Operation} received invalid base64 data", nameof(Verify));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return Task.FromResult(new VerifyFingerPrintsResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "The input is not a valid Base-64 string."
                });
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "{Operation} received a non-BMP or malformed image", nameof(Verify));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return Task.FromResult(new VerifyFingerPrintsResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "Not valid biometric data or invalid format."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Verify));
                Response.StatusCode = BiometricResponseCode.ServiceUnavailable;
                return Task.FromResult(new VerifyFingerPrintsResponse
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = "Something went wrong, please try again later"
                });
            }
        }
        #endregion

        #region fingerprint Enroll
        /// <summary>
        /// Enrolls a fingerprint template against an existing identity for later search/verification.
        /// </summary>
        /// <param name="request">The identity to enroll against and the fingerprint image(s) to store.</param>
        /// <returns>The enrolled identity id.</returns>
        [HttpPost]
        [ApiExplorerSettings(GroupName = "BiometricAPI")]
        [Route("Enroll", Name = "EnrollFingerPrint")]
        [APIAuthorizationFilter]
        public Task<BiometricEnrollResponse> Enroll([FromBody] FingerPrintEnrollRequest request)
        {
            BiometricEnrollResponse response = new BiometricEnrollResponse();
            try
            {
                if (request == null || string.IsNullOrEmpty(request.FingerPrint) || string.IsNullOrEmpty(request.ExternalId) || string.IsNullOrEmpty(request.Group))
                {
                    response.ErrorMessage = InvalidRequestMessage;
                    response.Code = BiometricResponseCode.BadRequest;
                    Response.StatusCode = BiometricResponseCode.BadRequest;
                    return Task.FromResult(response);
                }

                // The master's real Enroll schema has no identity field of its own — it echoes
                // back request.ExternalId (sent as demographics.referenceId) inside the response's
                // "data.referenceId", which BiometricEnrollResponse.IdentityId maps to.
                var (width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.GetBmpDimensions(request.FingerPrint);
                var masterPayload = new FingerPrintEnrollMasterPayload
                {
                    GroupName = request.Group,
                    Demographics = new DemographicsPayload { ReferenceId = request.ExternalId },
                    Fingerprints = new FingerprintsPayload
                    {
                        BioData = new List<BioData>
                        {
                            new BioData { Format = "BMP", Version = BmpBioDataVersion, Wd = width, Ht = height, Data = request.FingerPrint }
                        }
                    }
                };

                return CallAsync<BiometricEnrollResponse>(
                    EnrollMode, masterPayload, (r, o) => _clientApiClient.EnrolAsync(r, o).ResponseAsync, nameof(Enroll),
                    BiometricFeatureType.FingerPrintEnroll,
                    // referenceId already has an identity (e.g. Face enrolled first for the same
                    // referenceId) — retry via Update to add/replace the Finger modality instead
                    // of surfacing the rejection.
                    shouldRetryWithFallback: MasterErrorMapper.IsAlreadyEnrolledError,
                    fallbackGrpcCall: (r, o) => _clientApiClient.UpdateAsync(r, o).ResponseAsync);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "{Operation} received invalid base64 data", nameof(Enroll));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return Task.FromResult(new BiometricEnrollResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "The input is not a valid Base-64 string."
                });
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "{Operation} received a non-BMP or malformed image", nameof(Enroll));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return Task.FromResult(new BiometricEnrollResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "Not valid biometric data or invalid format."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Enroll));
                Response.StatusCode = BiometricResponseCode.ServiceUnavailable;
                return Task.FromResult(new BiometricEnrollResponse
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = "Something went wrong, please try again later"
                });
            }
        }
        #endregion

        #region fingerprint Search
        /// <summary>
        /// Searches a fingerprint image against enrolled identities (1:N identification).
        /// </summary>
        /// <param name="request">The fingerprint image to search with, optionally scoped to a group.</param>
        /// <returns>The best-matching identity, if any, and its matching score.</returns>
        [HttpPost]
        [ApiExplorerSettings(GroupName = "BiometricAPI")]
        [Route("Search", Name = "SearchFingerPrint")]
        [APIAuthorizationFilter]
        public Task<BiometricSearchResponse> Search([FromBody] FingerPrintSearchRequest request)
        {
            BiometricSearchResponse response = new BiometricSearchResponse();

            if (request == null || string.IsNullOrEmpty(request.FingerPrint) || string.IsNullOrEmpty(request.Group))
            {
                response.ErrorMessage = InvalidRequestMessage;
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return Task.FromResult(response);
            }

            try
            {
                var (width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.GetBmpDimensions(request.FingerPrint);
                var masterPayload = new FingerPrintSearchMasterPayload
                {
                    GroupName = request.Group,
                    Fingerprints = new FingerprintsPayload
                    {
                        BioData = new List<BioData>
                        {
                            new BioData { Format = "BMP", Version = BmpBioDataVersion, Wd = width, Ht = height, Data = request.FingerPrint }
                        }
                    }
                };

                return CallAsync<BiometricSearchResponse>(
                    SearchMode, masterPayload, (r, o) => _clientApiClient.IdentifyAsync(r, o).ResponseAsync, nameof(Search),
                    BiometricFeatureType.FingerPrintSearch);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "{Operation} received invalid base64 data", nameof(Search));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return Task.FromResult(new BiometricSearchResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "The input is not a valid Base-64 string."
                });
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "{Operation} received a non-BMP or malformed image", nameof(Search));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return Task.FromResult(new BiometricSearchResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "Not valid biometric data or invalid format."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Search));
                Response.StatusCode = BiometricResponseCode.ServiceUnavailable;
                return Task.FromResult(new BiometricSearchResponse
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = "Something went wrong, please try again later"
                });
            }
        }
        #endregion

        #region fingerprint Delete
        /// <summary>
        /// Deletes the enrolled fingerprint template for an identity.
        /// </summary>
        /// <param name="request">The identity whose enrolled fingerprint template should be removed.</param>
        /// <returns>Whether the delete succeeded.</returns>
        [HttpPost]
        [ApiExplorerSettings(GroupName = "BiometricAPI")]
        [Route("Delete", Name = "DeleteFingerPrint")]
        [APIAuthorizationFilter]
        public Task<BiomatricBaseResponse> Delete([FromBody] FingerPrintDeleteRequest request)
        {
            BiomatricBaseResponse response = new BiomatricBaseResponse();

            if (request == null || string.IsNullOrEmpty(request.ExternalId) || string.IsNullOrEmpty(request.Group))
            {
                response.ErrorMessage = InvalidRequestMessage;
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return Task.FromResult(response);
            }

            try
            {
                var masterPayload = new DeleteMasterPayload
                {
                    GroupName = request.Group,
                    ReferenceIds = new List<string> { request.ExternalId }
                };

                return CallAsync<BiomatricBaseResponse>(
                    DeleteMode, masterPayload, (r, o) => _clientApiClient.DeleteAsync(r, o).ResponseAsync, nameof(Delete),
                    BiometricFeatureType.FingerPrintDelete);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Delete));
                Response.StatusCode = BiometricResponseCode.ServiceUnavailable;
                return Task.FromResult(new BiomatricBaseResponse
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = "Something went wrong, please try again later"
                });
            }
        }
        #endregion

        #region fingerprint Liveness
        /// <summary>
        /// Checks whether a submitted fingerprint image is from a live person rather than a spoof (anti-spoofing).
        /// </summary>
        /// <param name="request">The fingerprint image to run the liveness check against.</param>
        /// <returns>Whether the image is considered live, and the liveness score.</returns>
        [HttpPost]
        [ApiExplorerSettings(GroupName = "BiometricAPI")]
        [Route("Liveness", Name = "CheckFingerPrintLiveness")]
        [APIAuthorizationFilter]
        public async Task<FingerprintLivenessResponse> Liveness([FromBody] FingerprintLivenessRequest request)
        {
            var response = new FingerprintLivenessResponse();

            if (request == null || string.IsNullOrEmpty(request.FingerPrintData))
            {
                response.ErrorMessage = InvalidRequestMessage;
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return response;
            }

            var requestTimestamp = DateTime.UtcNow;
            var clientId = ResolvedClientId ?? 0;
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            var reqId = Guid.NewGuid().ToString();
            var requestPayloadJson = string.Empty;

            // Liveness calls BioAnalyzeAsync directly rather than through CallAsync (BioAnalyze is
            // its own RPC, not the generic ClientApiRequest envelope), so it never hit the one
            // place that writes faceclient_db.transactions — this local function reuses the same
            // safe LogTransactionSafeAsync helper CallAsync itself uses.
            async Task LogLivenessTransactionAsync(string responsePayload, string error, string errorPoint) =>
                await LogTransactionSafeAsync(new TransactionLogEntry
                {
                    ClientId = clientId,
                    ReqId = Truncate(reqId, 36),
                    RequestPayload = requestPayloadJson,
                    RequestTimestamp = requestTimestamp,
                    ResponsePayload = responsePayload,
                    Error = Truncate(error, 10),
                    ResponseTimestamp = DateTime.UtcNow,
                    ErrorPoint = errorPoint,
                    FeatureType = BiometricFeatureType.FingerPrintLiveness,
                    QuotaCount = 1,
                    ClientIp = Truncate(clientIp, 50),
                    CreatedBy = clientId
                });

            try
            {
                // Same BioAnalyze RPC as Quality (see that action's comment for why — it's a
                // directly-typed RPC, not the generic ClientApiRequest envelope), with
                // FeatureFlags.Liveness instead of Quality.
                var (format, width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(request.FingerPrintData);
                var imageBytes = Convert.FromBase64String(request.FingerPrintData);

                var analyzeImage = new AnalyzeImage
                {
                    Data = ByteString.CopyFrom(imageBytes),
                    Format = MapBioFormatCode(format),
                    Position = FingerPosition,
                    Width = width ?? 0,
                    Height = height ?? 0
                };

                var bioAnalyzeRequest = new BioAnalyzeRequest
                {
                    SubscriptionKey = Request.Headers["subscriptionkey"].ToString(),
                    ReqId = reqId,
                    Modalities =
                    {
                        new ModalityInput
                        {
                            Modality = FinagerModality,
                            Images = { analyzeImage },
                            Features = new FeatureFlags { Liveness = true, Quality = true }
                        }
                    }
                };
                requestPayloadJson = JsonFormatter.Default.Format(bioAnalyzeRequest);

                var grpcResponse = await _clientApiClient.BioAnalyzeAsync(bioAnalyzeRequest);
                var responseJson = JsonFormatter.Default.Format(grpcResponse);

                if (grpcResponse.Ec != 0)
                {
                    _logger.LogError("BioAnalyze (Liveness) rejected: ec={Ec} em={Em}", grpcResponse.Ec, grpcResponse.Em);
                    response.Code = BiometricResponseCode.ServiceUnavailable;
                    response.ErrorMessage = grpcResponse.Em;
                    await LogLivenessTransactionAsync(responseJson, grpcResponse.Ec.ToString(), "MASTER");
                    return response;
                }

                var modalityResult = grpcResponse.Results.FirstOrDefault();
                if (modalityResult == null || modalityResult.Ec != 0 || modalityResult.Fingers.Count == 0)
                {
                    _logger.LogError("BioAnalyze (Liveness) returned no usable fingerprint result (modality ec={Ec})", modalityResult?.Ec);
                    response.Code = BiometricResponseCode.ServiceUnavailable;
                    response.ErrorMessage = "Fingerprint liveness could not be determined.";
                    await LogLivenessTransactionAsync(responseJson, modalityResult?.Ec.ToString() ?? "MODALITY", "MASTER");
                    return response;
                }

                var fingerResult = modalityResult.Fingers[0];
                if (fingerResult.Liveness == null)
                {
                    _logger.LogError("BioAnalyze (Liveness) returned a fingerprint with no Liveness result populated");
                    response.Code = BiometricResponseCode.ServiceUnavailable;
                    response.ErrorMessage = "Fingerprint liveness could not be determined.";
                    await LogLivenessTransactionAsync(responseJson, "NO_LIVENESS", "MASTER");
                    return response;
                }

                // TODO: ALiveness.Score (extract.proto) is an unscaled int32 — same 0-100 vs 0-1
                // assumption flagged on FaceController.Liveness, pending ABIS/master confirmation.
                response.IsLive = fingerResult.Liveness.Live;
                response.LivenessScore = fingerResult.Liveness.Score;
                //response.LivenessScore = fingerResult.Liveness.Score / 100f;
                response.ExtractedQuality = fingerResult.Quality;
                response.BiometricStatus = fingerResult.Ec != 0
                    ? $"Error({fingerResult.Ec})"
                    : (fingerResult.Liveness.Live ? "Live" : "NotLive");
                response.Message = grpcResponse.Em;
                response.Code = BiometricResponseCode.Success;
                await LogLivenessTransactionAsync(responseJson, string.Empty, string.Empty);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC BioAnalyze (Liveness) call failed");
                response.Code = BiometricResponseCode.ServiceUnavailable;
                response.ErrorMessage = "Fingerprint liveness service is unavailable.";
                await LogLivenessTransactionAsync("{}", ex.StatusCode.ToString(), "GRPC");
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Liveness received invalid base64 data");
                response.Code = BiometricResponseCode.BadRequest;
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidBase64;
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "Liveness received an unrecognized or malformed image");
                response.Code = BiometricResponseCode.BadRequest;
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidImage;
            }
            return response;
        }
        #endregion
    }
}
