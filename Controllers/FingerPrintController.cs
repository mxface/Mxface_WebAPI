using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MxfaceWebAPI.Common;
using MxfaceWebAPI.Data;
using MxfaceWebAPI.Filters;
using MxfaceWebAPI.Grpc;
using MxfaceWebAPI.Grpc.AbisClient;
using MxfaceWebAPI.Models;

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
        public Task<FingerprintLivenessResponse> Liveness([FromBody] FingerprintLivenessRequest request)
        {
            var response = new FingerprintLivenessResponse();
            try
            {
                if (request == null || string.IsNullOrEmpty(request.FingerPrintData))
                {
                    response.ErrorMessage = InvalidRequestMessage;
                    response.Code = BiometricResponseCode.BadRequest;
                    Response.StatusCode = BiometricResponseCode.BadRequest;
                    return Task.FromResult(response);
                }
                return CallAsync<FingerprintLivenessResponse>(
                    LivenessMode, request, (r, o) => _clientApiClient.MatchAsync(r, o).ResponseAsync, nameof(Liveness),
                    BiometricFeatureType.FingerPrintLiveness);
            }
            catch (Exception)
            {
                throw;
            }         
        }
        #endregion
    }
}
