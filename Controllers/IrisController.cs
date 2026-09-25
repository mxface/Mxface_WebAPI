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
using MxfaceWebAPI.Services;
using AnalyzeImage = MxfaceWebAPI.Grpc.Extract.AnalyzeImage;
using FeatureFlags = MxfaceWebAPI.Grpc.Extract.FeatureFlags;

namespace MxfaceWebAPI.Controllers
{
    // Subscription-key auth (via [APIAuthorizationFilter] on each action) gates these endpoints,
    // not the global JWT policy — AllowAnonymous opts out of that so the filter is what runs.
    [AllowAnonymous]
    [Route("api/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "Iris API")]
    public class IrisController : BiometricControllerBase
    {
        // TODO: confirm the real mode value the master uses for each Iris operation — see
        // FaceController.QualityCheckMode for the same open question on the Face side.
        private const int VerifyMode = 0;
        private const int EnrollMode = 0;
        private const int SearchMode = 0;
        private const int DeleteMode = 0;
        private const int LivenessMode = 0;
        private const int IrisModality = 2;

        // TODO: same unconfirmed-placeholder caveat as Face/Finger's BioFormat/BioPosition codes —
        // neither proto file defines what these integers mean beyond BioModality.
        private const int IrisPosition = 0;
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
        private readonly IEmailService _emailService;



        #region For Refrence Call
        public IrisController(
            ClientApiService.ClientApiServiceClient clientApiClient,
            IClientApiEnvelopeFactory envelopeFactory,
            IConfiguration configuration,
            IPostgresHelper postgresHelper,
            ILogger<IrisController> logger,
            IEmailService emailService)
            : base(envelopeFactory, configuration, postgresHelper, logger)
        {
            _clientApiClient = clientApiClient;
            _emailService = emailService;
        }
        #endregion

        #region iris Verify
        /// <summary>
        /// Compares two iris images and reports whether they belong to the same person (1:1 match).
        /// </summary>
        /// <param name="request">The two iris images to compare.</param>
        /// <returns>The matching score and whether the pair is considered a match.</returns>
        [ApiExplorerSettings(GroupName = "BiometricAPI")]
        [APIAuthorizationFilter]
        [Route("Verify", Name = "VerifyIris")]
        [HttpPost]
        public async Task<VerifyIrisResponse> Verify([FromBody] VerifyIrisRequest request)
        {
            VerifyIrisResponse response = new VerifyIrisResponse();

            if (request == null || string.IsNullOrEmpty(request.Iris1) || string.IsNullOrEmpty(request.Iris2))
            {
                response.ErrorMessage = InvalidRequestMessage;
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return response;
            }

            try
            {
                var (format1,width1, height1) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(request.Iris1);
                var (format2,width2, height2) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(request.Iris2);
                var masterPayload = new IrisVerifyMasterPayload
                {
                    Probe = new IrisProbeGalleryPayload
                    {
                        irises = new FingerprintsPayload
                        {
                            BioData = new List<BioData>
                            {
                                new BioData { Format = format1, Version = BmpBioDataVersion, Wd = width1, Ht = height1, Data = request.Iris1 }
                            }
                        }
                    },
                    Gallery = new IrisProbeGalleryPayload
                    {
                        irises = new FingerprintsPayload
                        {
                            BioData = new List<BioData>
                            {
                                new BioData { Format = format2, Version = BmpBioDataVersion, Wd = width2, Ht = height2, Data = request.Iris2 }
                            }
                        }
                    }
                };

                return await CallAsync<VerifyIrisResponse>(
                    VerifyMode, masterPayload, (r, o) => _clientApiClient.MatchAsync(r, o).ResponseAsync, nameof(Verify),
                    BiometricFeatureType.IrisVerify);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "{Operation} received invalid base64 data", nameof(Verify));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return new VerifyIrisResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "The input is not a valid Base-64 string."
                };
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "{Operation} received a non-BMP or malformed image", nameof(Verify));
                await _emailService.ExceptionMailSend(nameof(Verify), ex).ConfigureAwait(true);
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return new VerifyIrisResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "Not valid biometric data or invalid format."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Verify));
                await _emailService.ExceptionMailSend(nameof(Verify), ex).ConfigureAwait(true);
                Response.StatusCode = BiometricResponseCode.ServiceUnavailable;
                return new VerifyIrisResponse
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = "Something went wrong, please try again later"
                };
            }
        }
        #endregion

        #region iris Enroll
        /// <summary>
        /// Enrolls an iris template against an existing identity for later search/verification.
        /// </summary>
        /// <param name="request">The identity to enroll against and the iris image(s) to store.</param>
        /// <returns>The enrolled identity id.</returns>
        [APIAuthorizationFilter]
        [HttpPost]
        [ApiExplorerSettings(GroupName = "BiometricAPI")]
        [Route("Enroll", Name = "EnrollIris")]
        public async Task<BiometricEnrollResponse> Enroll([FromBody] IrisEnrollRequest request)
        {
            BiometricEnrollResponse response = new BiometricEnrollResponse();
            try
            {

                if (request == null || string.IsNullOrEmpty(request.Iris) || string.IsNullOrEmpty(request.ExternalId) || string.IsNullOrEmpty(request.Group))
                {
                    response.ErrorMessage = InvalidRequestMessage;
                    response.Code = BiometricResponseCode.BadRequest;
                    Response.StatusCode = BiometricResponseCode.BadRequest;
                    return response;
                }

                // The master's real Enroll schema has no identity field of its own — it echoes
                // back request.ExternalId (sent as demographics.referenceId) inside the response's
                // "data.referenceId", which BiometricEnrollResponse.IdentityId maps to.
                var (format,width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(request.Iris);
                var masterPayload = new IrisEnrollMasterPayload
                {
                    GroupName = request.Group,
                    Demographics = new DemographicsPayload { ReferenceId = request.ExternalId },
                    Irises = new FingerprintsPayload
                    {
                        BioData = new List<BioData>
                        {
                            new BioData { Format = format, Version = BmpBioDataVersion, Wd = width, Ht = height, Data = request.Iris }
                        }
                    }
                };

                return await CallAsync<BiometricEnrollResponse>(
                    EnrollMode, masterPayload, (r, o) => _clientApiClient.EnrolAsync(r, o).ResponseAsync, nameof(Enroll),
                    BiometricFeatureType.IrisEnroll,
                    // referenceId already has an identity (e.g. Face enrolled first for the same
                    // referenceId) — retry via Update to add/replace the Iris modality instead of
                    // surfacing the rejection.
                    shouldRetryWithFallback: MasterErrorMapper.IsAlreadyEnrolledError,
                    fallbackGrpcCall: (r, o) => _clientApiClient.UpdateAsync(r, o).ResponseAsync);

            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "{Operation} received invalid base64 data", nameof(Enroll));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return new BiometricEnrollResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "The input is not a valid Base-64 string."
                };
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "{Operation} received a non-BMP or malformed image", nameof(Enroll));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return new BiometricEnrollResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "Not valid biometric data or invalid format."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Enroll));
                await _emailService.ExceptionMailSend(nameof(Enroll), ex).ConfigureAwait(true);
                Response.StatusCode = BiometricResponseCode.ServiceUnavailable;
                return new BiometricEnrollResponse
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = "Something went wrong, please try again later"
                };
            }
        }
        #endregion

        #region iris Search
        /// <summary>
        /// Searches an iris image against enrolled identities (1:N identification).
        /// </summary>
        /// <param name="request">The iris image to search with, optionally scoped to a group.</param>
        /// <returns>The best-matching identity, if any, and its matching score.</returns>
        [APIAuthorizationFilter]
        [HttpPost]
        [ApiExplorerSettings(GroupName = "BiometricAPI")]
        [Route("Search", Name = "SearchIris")]
        public async Task<BiometricSearchResponse> Search([FromBody] IrisSearchRequest request)
        {
            BiometricSearchResponse response = new BiometricSearchResponse();

            if (request == null || string.IsNullOrEmpty(request.Iris) || string.IsNullOrEmpty(request.Group))
            {
                response.ErrorMessage = InvalidRequestMessage;
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return response;
            }

            try
            {
                var (format,width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(request.Iris);
                var masterPayload = new IrisSearchMasterPayload
                {
                    GroupName = request.Group,
                    Irises = new FingerprintsPayload
                    {
                        BioData = new List<BioData>
                        {
                            new BioData { Format = format, Version = BmpBioDataVersion, Wd = width, Ht = height, Data = request.Iris }
                        }
                    }
                };

                return await CallAsync<BiometricSearchResponse>(
                    SearchMode, masterPayload, (r, o) => _clientApiClient.IdentifyAsync(r, o).ResponseAsync, nameof(Search),
                    BiometricFeatureType.IrisSearch);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "{Operation} received invalid base64 data", nameof(Search));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return new BiometricSearchResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "The input is not a valid Base-64 string."
                };
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "{Operation} received a non-BMP or malformed image", nameof(Search));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return new BiometricSearchResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = "Not valid biometric data or invalid format."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Search));
                await _emailService.ExceptionMailSend(nameof(Search), ex).ConfigureAwait(true);
                Response.StatusCode = BiometricResponseCode.ServiceUnavailable;
                return new BiometricSearchResponse
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = "Something went wrong, please try again later"
                };
            }
        }
        #endregion

        #region iris Delete
        /// <summary>
        /// Deletes the enrolled iris template for an identity.
        /// </summary>
        /// <param name="request">The identity whose enrolled iris template should be removed.</param>
        /// <returns>Whether the delete succeeded.</returns>
        [APIAuthorizationFilter]
        [HttpPost]
        [ApiExplorerSettings(GroupName = "BiometricAPI")]
        [Route("Delete", Name = "DeleteIris")]
        public async Task<BiomatricBaseResponse> Delete([FromBody] IrisDeleteRequest request)
        {
            BiomatricBaseResponse response = new BiomatricBaseResponse();

            if (request == null || string.IsNullOrEmpty(request.ExternalId) || string.IsNullOrEmpty(request.Group))
            {
                response.ErrorMessage = InvalidRequestMessage;
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return response;
            }

            try
            {
                var masterPayload = new DeleteMasterPayload
                {
                    GroupName = request.Group,
                    ReferenceIds = new List<string> { request.ExternalId }
                };

                return await CallAsync<BiomatricBaseResponse>(
                    DeleteMode, masterPayload, (r, o) => _clientApiClient.DeleteAsync(r, o).ResponseAsync, nameof(Delete),
                    BiometricFeatureType.IrisDelete);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Delete));
                await _emailService.ExceptionMailSend(nameof(Delete), ex).ConfigureAwait(true);
                Response.StatusCode = BiometricResponseCode.ServiceUnavailable;
                return new BiomatricBaseResponse
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = "Something went wrong, please try again later"
                };
            }
        }
        #endregion

        #region iris Liveness
        /// <summary>
        /// Checks whether a submitted iris image is from a live person rather than a spoof (anti-spoofing).
        /// </summary>
        /// <param name="request">The iris image to run the liveness check against.</param>
        /// <returns>Whether the image is considered live, and the liveness score.</returns>
        [HttpPost]
        [ApiExplorerSettings(GroupName = "BiometricAPI")]
        [Route("Liveness", Name = "CheckIrisLiveness")]
        [APIAuthorizationFilter]
        public async Task<IrisLivenessResponse> Liveness([FromBody] IrisLivenessRequest request)
        {
            var response = new IrisLivenessResponse();

            if (request == null || string.IsNullOrEmpty(request.IrisData))
            {
                response.ErrorMessage = "Iris image (base64) is required.";
                response.Code = 400;
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
                    FeatureType = BiometricFeatureType.IrisLiveness,
                    QuotaCount = 1,
                    ClientIp = Truncate(clientIp, 50),
                    CreatedBy = clientId
                });

            try
            {
                // Same BioAnalyze RPC as FingerPrintController.Liveness (see that action's comment
                // for why — it's a directly-typed RPC, not the generic ClientApiRequest envelope).
                var (format, width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(request.IrisData);
                var imageBytes = Convert.FromBase64String(request.IrisData);

                var analyzeImage = new AnalyzeImage
                {
                    Data = ByteString.CopyFrom(imageBytes),
                    Format = MapBioFormatCode(format),
                    Position = IrisPosition,
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
                            Modality = IrisModality,
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
                if (modalityResult == null || modalityResult.Ec != 0 || modalityResult.Irises.Count == 0)
                {
                    _logger.LogError("BioAnalyze (Liveness) returned no usable iris result (modality ec={Ec})", modalityResult?.Ec);
                    response.Code = BiometricResponseCode.ServiceUnavailable;
                    response.ErrorMessage = "Iris liveness could not be determined.";
                    await LogLivenessTransactionAsync(responseJson, modalityResult?.Ec.ToString() ?? "MODALITY", "MASTER");
                    return response;
                }

                var irisResult = modalityResult.Irises[0];
                if (irisResult.Liveness == null)
                {
                    _logger.LogError("BioAnalyze (Liveness) returned an iris with no Liveness result populated");
                    response.Code = BiometricResponseCode.ServiceUnavailable;
                    response.ErrorMessage = "Iris liveness could not be determined.";
                    await LogLivenessTransactionAsync(responseJson, "NO_LIVENESS", "MASTER");
                    return response;
                }

                // TODO: ALiveness.Score (extract.proto) is an unscaled int32 — same 0-100 vs 0-1
                // assumption flagged on Face/FingerPrint Liveness, pending ABIS/master confirmation.
                response.IsLive = irisResult.Liveness.Live;
                response.LivenessScore = irisResult.Liveness.Score / 100f;
                response.ExtractedQuality = irisResult.Quality;
                response.BiometricStatus = irisResult.Ec != 0
                    ? $"Error({irisResult.Ec})"
                    : (irisResult.Liveness.Live ? "Live" : "NotLive");
                response.Message = grpcResponse.Em;
                response.Code = BiometricResponseCode.Success;
                await LogLivenessTransactionAsync(responseJson, string.Empty, string.Empty);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC BioAnalyze (Liveness) call failed");
                response.Code = BiometricResponseCode.ServiceUnavailable;
                response.ErrorMessage = "Iris liveness service is unavailable.";
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Liveness));
                await _emailService.ExceptionMailSend(nameof(Liveness), ex).ConfigureAwait(true);
                response.Code = BiometricResponseCode.ServiceUnavailable;
                response.ErrorMessage = "Something went wrong, please try again later";
                await LogLivenessTransactionAsync("{}", ex.GetType().Name, "UNEXPECTED");
            }
            return response;
        }
        #endregion
    }
}
