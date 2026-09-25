using Asp.Versioning;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MxfaceWebAPI.Common;
using MxfaceWebAPI.Data;
using MxfaceWebAPI.Filters;
using MxfaceWebAPI.Grpc;
using MxfaceWebAPI.Grpc.AbisClient;
using MxfaceWebAPI.Grpc.Extract;
using MxfaceWebAPI.Models;
using MxfaceWebAPI.Models.Request.Face;
using MxfaceWebAPI.Models.Response;
using Newtonsoft.Json;
using System;
using System.IO.Compression;
using System.Text.Json;

namespace MxfaceWebAPI.Controllers.V3
{
    [AllowAnonymous]
    [ApiVersion("3.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class FaceController : BiometricControllerBase
    {
        // BioModality code — confirmed directly in abis_client.proto/extract.proto comments:
        // 1=FINGER 2=IRIS 3=FACE.
        private const int FaceModality = 3;

        // TODO: confirm the real BioFormat/BioPosition integer codes with the ABIS/master team —
        // neither abis_client.proto nor extract.proto defines what these integers mean (only
        // BioModality is spelled out). These are placeholders so BioAnalyze wiring can ship and be
        // corrected from real master feedback, same as this project's other flagged placeholders
        // (see CLAUDE.md "Known placeholder/unconfirmed values").
        private const int BioFormatBmp = 0;
        private const int BioFormatJpeg = 1;
        private const int BioFormatPng = 2;
        private const int FacePosition = 0;

        private const int VerifyMode = 0;

        // Per the official ABIS Client API v2.0 reference doc's Biometric Data Formats table
        // (§21): raster formats (BMP/JPEG/PNG/RAW/WSQ) all pair with version="0" regardless of
        // which one — only the ISO FIR/IIR/FID record formats use "2005"/"2011".
        private const string ImageBioDataVersion = "0";

        private readonly ClientApiService.ClientApiServiceClient _clientApiClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FaceController> _logger;
        private readonly IPostgresHelper _postgresHelper;
        public static bool _IsEnabledPassiveLiveness = true;

        #region For Refrence Call
        public FaceController(
            ClientApiService.ClientApiServiceClient clientApiClient,
            IConfiguration configuration,
            ILogger<FaceController> logger,
            IPostgresHelper postgresHelper,
            IClientApiEnvelopeFactory envelopeFactory)
            : base(envelopeFactory, configuration, postgresHelper, logger)
        {
            _clientApiClient = clientApiClient;
            _configuration = configuration;
            _logger = logger;
            _postgresHelper = postgresHelper;
        }
        #endregion

        // Resolved by APIAuthorizationFilterAttribute and stashed in HttpContext.Items — same
        // pattern as BiometricControllerBase.ResolvedClientId, duplicated here since this
        // controller inherits ControllerBase directly, not BiometricControllerBase.
        private long? ResolvedClientId =>
            HttpContext.Items.TryGetValue("ClientId", out var value) && value is long clientId ? clientId : null;

        #region For QualityAPI Call

        [MapToApiVersion("3.0")]
        [HttpPost("Quality", Name = "Quality")]
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [APIAuthorizationFilter]
        public async Task<ActionResult<FaceDetect>> Quality([FromBody] QualityRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EncodedImage))
            {
                return StatusCode(BiometricResponseCode.BadRequest, new QualityResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest
                });
            }

            // BioAnalyze-based actions call the RPC directly (not through
            // BiometricControllerBase.CallAsync, since this controller doesn't derive from it), so
            // they never hit the one place that writes faceclient_db.transactions — LogFaceTransactionAsync
            // (below) reuses the same safe LogTransactionSafeAsync helper CallAsync itself uses.
            var requestTimestamp = DateTime.UtcNow;
            var reqId = Guid.NewGuid().ToString();
            var requestPayloadJson = string.Empty;

            try
            {
                // BioAnalyze is its own directly-typed RPC (not the generic ClientApiRequest
                // envelope Match/Enrol/etc. use) — was previously (wrongly) calling MatchAsync, a
                // 1:1 verify RPC expecting a probe/gallery payload, which the master correctly
                // rejected (confirmed live: ec=-1017 "Probe biometric data is required").
                var (format, width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(request.EncodedImage);
                var imageBytes = Convert.FromBase64String(request.EncodedImage);

                var analyzeImage = new AnalyzeImage
                {
                    Data = ByteString.CopyFrom(imageBytes),
                    Format = MapBioFormatCode(format),
                    Position = FacePosition,
                    Width = width ?? 0,
                    Height = height ?? 0
                };

                var bioAnalyzeRequest = new BioAnalyzeRequest
                {
                    // Forward the caller's own key (same header APIAuthorizationFilterAttribute
                    // reads) instead of a static config value — matches every other controller.
                    SubscriptionKey = Request.Headers["subscriptionkey"].ToString(),
                    ReqId = reqId,
                    Modalities =
                    {
                        new ModalityInput
                        {
                            Modality = FaceModality,
                            Images = { analyzeImage },
                            Features = new FeatureFlags { Quality = true }
                        }
                    }
                };
                requestPayloadJson = JsonFormatter.Default.Format(bioAnalyzeRequest);

                var grpcResponse = await _clientApiClient.BioAnalyzeAsync(bioAnalyzeRequest);
                var responseJson = JsonFormatter.Default.Format(grpcResponse);

                if (grpcResponse.Ec != 0)
                {
                    _logger.LogError("BioAnalyze (Quality) rejected: ec={Ec} em={Em}", grpcResponse.Ec, grpcResponse.Em);
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceQuality, reqId, requestTimestamp, requestPayloadJson, responseJson, grpcResponse.Ec.ToString(), "MASTER");
                    return StatusCode(BiometricResponseCode.ServiceUnavailable, new QualityResponse
                    {
                        Code = BiometricResponseCode.ServiceUnavailable,
                        ErrorMessage = grpcResponse.Em
                    });
                }

                var modalityResult = grpcResponse.Results.FirstOrDefault();
                if (modalityResult == null || modalityResult.Ec != 0 || modalityResult.Faces.Count == 0)
                {
                    _logger.LogError("BioAnalyze (Quality) returned no usable face result (modality ec={Ec})", modalityResult?.Ec);
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceQuality, reqId, requestTimestamp, requestPayloadJson, responseJson, modalityResult?.Ec.ToString() ?? "MODALITY", "MASTER");
                    return StatusCode(BiometricResponseCode.ServiceUnavailable, new QualityResponse
                    {
                        Code = BiometricResponseCode.ServiceUnavailable,
                        ErrorMessage = "Face quality could not be determined."
                    });
                }

                var faceResult = modalityResult.Faces[0];
                var face = new FaceDetect { Quality = faceResult.Quality };
                await LogFaceTransactionAsync(BiometricFeatureType.FaceQuality, reqId, requestTimestamp, requestPayloadJson, responseJson, string.Empty, string.Empty);
                return Ok(face);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC BioAnalyze (Quality) call failed");
                await LogFaceTransactionAsync(BiometricFeatureType.FaceQuality, reqId, requestTimestamp, requestPayloadJson, "{}", ex.StatusCode.ToString(), "GRPC");
                return StatusCode(BiometricResponseCode.ServiceUnavailable, new QualityResponse
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = "Face quality service is unavailable."
                });
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Quality received invalid base64 data");
                return StatusCode(BiometricResponseCode.BadRequest, new QualityResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidBase64
                });
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "Quality received an unrecognized or malformed image");
                return StatusCode(BiometricResponseCode.BadRequest, new QualityResponse
                {
                    Code = BiometricResponseCode.BadRequest,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidImage
                    
                });
            }
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
            FaceAnalyticsResponse faceAnalyticsResponse = new FaceAnalyticsResponse();

            if (model == null || string.IsNullOrWhiteSpace(model.encoded_image))
            {
                faceAnalyticsResponse.ErrorCode = 400;
                faceAnalyticsResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
                return await ReturnResponse(faceAnalyticsResponse).ConfigureAwait(true);
            }

            var requestTimestamp = DateTime.UtcNow;
            var reqId = Guid.NewGuid().ToString();
            var requestPayloadJson = string.Empty;

            try
            {
                // Same BioAnalyze RPC as Quality/Liveness, requesting Detect (+ Landmark for
                // eye/nose/mouth points, + Crop only when the caller asked for a cropped face).
                var (format, width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(model.encoded_image);
                var imageBytes = Convert.FromBase64String(model.encoded_image);

                var analyzeImage = new AnalyzeImage
                {
                    Data = ByteString.CopyFrom(imageBytes),
                    Format = MapBioFormatCode(format),
                    Position = FacePosition,
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
                            Modality = FaceModality,
                            Images = { analyzeImage },
                            Features = new FeatureFlags { Detect = true, Landmark = true, Crop = model.GetCroppedFace, Quality = true, Gender = true, Age = true, Emotion = true }
                        }
                    }
                };
                requestPayloadJson = JsonFormatter.Default.Format(bioAnalyzeRequest);

                var grpcResponse = await _clientApiClient.BioAnalyzeAsync(bioAnalyzeRequest);
                var responseJson = JsonFormatter.Default.Format(grpcResponse);

                if (grpcResponse.Ec != 0)
                {
                    _logger.LogError("BioAnalyze (Analytics) rejected: ec={Ec} em={Em}", grpcResponse.Ec, grpcResponse.Em);
                    faceAnalyticsResponse.ErrorCode = 500;
                    faceAnalyticsResponse.ErrorMessage = grpcResponse.Em;
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceAnalytics, reqId, requestTimestamp, requestPayloadJson, responseJson, grpcResponse.Ec.ToString(), "MASTER");
                    return await ReturnResponse(faceAnalyticsResponse).ConfigureAwait(true);
                }

                var modalityResult = grpcResponse.Results.FirstOrDefault();
                if (modalityResult == null || modalityResult.Ec != 0)
                {
                    _logger.LogError("BioAnalyze (Analytics) returned a modality-level error (ec={Ec})", modalityResult?.Ec);
                    faceAnalyticsResponse.ErrorCode = 500;
                    faceAnalyticsResponse.ErrorMessage = "Face analytics could not be determined.";
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceAnalytics, reqId, requestTimestamp, requestPayloadJson, responseJson, modalityResult?.Ec.ToString() ?? "MODALITY", "MASTER");
                    return await ReturnResponse(faceAnalyticsResponse).ConfigureAwait(true);
                }

                // No face found is a valid empty result for a detection endpoint, not an error.
                faceAnalyticsResponse.Faces = modalityResult.Faces.Select(f => new Analytics
                {
                    Quality = f.Quality,
                    // f.Rect is a protobuf message-type field — null when the master doesn't
                    // populate it (e.g. a degenerate/non-real-face image), not just a default struct.
                    FaceRectangle = f.Rect == null ? null : new FaceRectangle { x = f.Rect.X, y = f.Rect.Y, width = f.Rect.W, height = f.Rect.H },
                    Points = f.Landmarks.Select(l => new Points { X = l.X, Y = l.Y }).ToList(),
                    croppedFace = model.GetCroppedFace && f.CroppedFace.Length > 0
                        ? Convert.ToBase64String(f.CroppedFace.ToByteArray())
                        : null,
                    FaceAnalytics = new FaceAnalytics
                    {
                        EstimatedAge = f.Age >= 0 ? f.Age.ToString() : null,
                        Gender = string.IsNullOrEmpty(f.Gender) ? null : f.Gender,
                        Emotion = string.IsNullOrEmpty(f.Emotion) ? null : f.Emotion,
                        Confidence = f.GenderConfidence
                    }
                }).ToList();
                faceAnalyticsResponse.Code = 200;
                await LogFaceTransactionAsync(BiometricFeatureType.FaceAnalytics, reqId, requestTimestamp, requestPayloadJson, responseJson, string.Empty, string.Empty);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC BioAnalyze (Analytics) call failed");
                faceAnalyticsResponse.ErrorCode = 500;
                faceAnalyticsResponse.ErrorMessage = "Face analytics service is unavailable.";
                await LogFaceTransactionAsync(BiometricFeatureType.FaceAnalytics, reqId, requestTimestamp, requestPayloadJson, "{}", ex.StatusCode.ToString(), "GRPC");
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Analytics received invalid base64 data");
                faceAnalyticsResponse.ErrorCode = 400;
                faceAnalyticsResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidBase64;
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "Analytics received an unrecognized or malformed image");
                faceAnalyticsResponse.ErrorCode = 400;
                faceAnalyticsResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidImage;
            }
            return await ReturnResponse(faceAnalyticsResponse).ConfigureAwait(true);
        }

        #region Face API Emotion

        /// <summary>
        /// get emotion detected for the face(s) in the image
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost("Emotion", Name = "Emotion")]
        [ApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [APIAuthorizationFilter]
        public async Task<ActionResult<FaceAnalyticsResponse>> Emotion([FromBody] DetectFace model)
        {
            FaceAnalyticsResponse faceAnalyticsResponse = new FaceAnalyticsResponse();

            if (model == null || string.IsNullOrWhiteSpace(model.encoded_image))
            {
                faceAnalyticsResponse.ErrorCode = 400;
                faceAnalyticsResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
                return await ReturnResponse(faceAnalyticsResponse).ConfigureAwait(true);
            }

            var requestTimestamp = DateTime.UtcNow;
            var reqId = Guid.NewGuid().ToString();
            var requestPayloadJson = string.Empty;

            try
            {
                // Same BioAnalyze RPC as Quality/Liveness/Analytics, requesting only Detect (to
                // localize the face) + Emotion.
                var (format, width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(model.encoded_image);
                var imageBytes = Convert.FromBase64String(model.encoded_image);

                var analyzeImage = new AnalyzeImage
                {
                    Data = ByteString.CopyFrom(imageBytes),
                    Format = MapBioFormatCode(format),
                    Position = FacePosition,
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
                            Modality = FaceModality,
                            Images = { analyzeImage },
                            Features = new FeatureFlags { Detect = true, Emotion = true }
                        }
                    }
                };
                requestPayloadJson = JsonFormatter.Default.Format(bioAnalyzeRequest);

                var grpcResponse = await _clientApiClient.BioAnalyzeAsync(bioAnalyzeRequest);
                var responseJson = JsonFormatter.Default.Format(grpcResponse);

                if (grpcResponse.Ec != 0)
                {
                    _logger.LogError("BioAnalyze (Emotion) rejected: ec={Ec} em={Em}", grpcResponse.Ec, grpcResponse.Em);
                    faceAnalyticsResponse.ErrorCode = 500;
                    faceAnalyticsResponse.ErrorMessage = grpcResponse.Em;
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceEmotion, reqId, requestTimestamp, requestPayloadJson, responseJson, grpcResponse.Ec.ToString(), "MASTER");
                    return await ReturnResponse(faceAnalyticsResponse).ConfigureAwait(true);
                }

                var modalityResult = grpcResponse.Results.FirstOrDefault();
                if (modalityResult == null || modalityResult.Ec != 0)
                {
                    _logger.LogError("BioAnalyze (Emotion) returned a modality-level error (ec={Ec})", modalityResult?.Ec);
                    faceAnalyticsResponse.ErrorCode = 500;
                    faceAnalyticsResponse.ErrorMessage = "Face emotion could not be determined.";
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceEmotion, reqId, requestTimestamp, requestPayloadJson, responseJson, modalityResult?.Ec.ToString() ?? "MODALITY", "MASTER");
                    return await ReturnResponse(faceAnalyticsResponse).ConfigureAwait(true);
                }

                // No face found is a valid empty result for a detection endpoint, not an error.
                // f.Rect is a protobuf message-type field — null when the master doesn't populate
                // it (e.g. a degenerate/non-real-face image), not just a default struct.
                faceAnalyticsResponse.Faces = modalityResult.Faces.Select(f => new Analytics
                {
                    FaceRectangle = f.Rect == null ? null : new FaceRectangle { x = f.Rect.X, y = f.Rect.Y, width = f.Rect.W, height = f.Rect.H },
                    FaceAnalytics = new FaceAnalytics
                    {
                        Emotion = string.IsNullOrEmpty(f.Emotion) ? null : f.Emotion
                    }
                }).ToList();
                faceAnalyticsResponse.Code = 200;
                await LogFaceTransactionAsync(BiometricFeatureType.FaceEmotion, reqId, requestTimestamp, requestPayloadJson, responseJson, string.Empty, string.Empty);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC BioAnalyze (Emotion) call failed");
                faceAnalyticsResponse.ErrorCode = 500;
                faceAnalyticsResponse.ErrorMessage = "Face emotion service is unavailable.";
                await LogFaceTransactionAsync(BiometricFeatureType.FaceEmotion, reqId, requestTimestamp, requestPayloadJson, "{}", ex.StatusCode.ToString(), "GRPC");
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Emotion received invalid base64 data");
                faceAnalyticsResponse.ErrorCode = 400;
                faceAnalyticsResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidBase64;
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "Emotion received an unrecognized or malformed image");
                faceAnalyticsResponse.ErrorCode = 400;
                faceAnalyticsResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidImage;
            }
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
            // FaceDetectResponse doesn't inherit BiomatricBaseResponse (no ErrorCode field), so
            // errors use the project's generic ApiErrorResponse{Code,Error} instead of ReturnResponse.
            if (model == null || string.IsNullOrWhiteSpace(model.encoded_image))
            {
                return StatusCode(400, new ApiErrorResponse { Code = 400, Error = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest });
            }

            var requestTimestamp = DateTime.UtcNow;
            var reqId = Guid.NewGuid().ToString();
            var requestPayloadJson = string.Empty;

            try
            {
                // Same BioAnalyze RPC as Quality/Liveness/Analytics/Emotion, requesting Detect
                // (+ Landmark for eye/nose/mouth points, + Crop only when asked) — no Quality/
                // Gender/Age/Emotion, this endpoint has no FaceAnalytics slot to populate.
                var (format, width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(model.encoded_image);
                var imageBytes = Convert.FromBase64String(model.encoded_image);

                var analyzeImage = new AnalyzeImage
                {
                    Data = ByteString.CopyFrom(imageBytes),
                    Format = MapBioFormatCode(format),
                    Position = FacePosition,
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
                            Modality = FaceModality,
                            Images = { analyzeImage },
                            Features = new FeatureFlags { Detect = true, Landmark = true,Quality=true, Crop = model.GetCroppedFace }
                        }
                    }
                };
                requestPayloadJson = JsonFormatter.Default.Format(bioAnalyzeRequest);

                var grpcResponse = await _clientApiClient.BioAnalyzeAsync(bioAnalyzeRequest);
                var responseJson = JsonFormatter.Default.Format(grpcResponse);

                if (grpcResponse.Ec != 0)
                {
                    _logger.LogError("BioAnalyze (Detect) rejected: ec={Ec} em={Em}", grpcResponse.Ec, grpcResponse.Em);
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceDetect, reqId, requestTimestamp, requestPayloadJson, responseJson, grpcResponse.Ec.ToString(), "MASTER");
                    return StatusCode(500, new ApiErrorResponse { Code = 500, Error = grpcResponse.Em });
                }

                var modalityResult = grpcResponse.Results.FirstOrDefault();
                if (modalityResult == null || modalityResult.Ec != 0)
                {
                    _logger.LogError("BioAnalyze (Detect) returned a modality-level error (ec={Ec})", modalityResult?.Ec);
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceDetect, reqId, requestTimestamp, requestPayloadJson, responseJson, modalityResult?.Ec.ToString() ?? "MODALITY", "MASTER");
                    return StatusCode(500, new ApiErrorResponse { Code = 500, Error = "Face detection could not be determined." });
                }

                // No face found is a valid empty result for a detection endpoint, not an error.
                var faceDetectResponse = new FaceDetectResponse
                {
                   
                    Faces = modalityResult.Faces.Select(f => new FaceDetect
                    {
                        Quality = f.Quality,
                        FaceRectangle = f.Rect == null ? null : new FaceRectangle { x = f.Rect.X, y = f.Rect.Y, width = f.Rect.W, height = f.Rect.H },
                        //Points = f.Landmarks.Select(l => new Points { X = l.X, Y = l.Y }).ToList(),
                        croppedFace = model.GetCroppedFace && f.CroppedFace.Length > 0
                            ? Convert.ToBase64String(f.CroppedFace.ToByteArray())
                            : null
                    }).ToList()
                };
                await LogFaceTransactionAsync(BiometricFeatureType.FaceDetect, reqId, requestTimestamp, requestPayloadJson, responseJson, string.Empty, string.Empty);
                return Ok(faceDetectResponse);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC BioAnalyze (Detect) call failed");
                await LogFaceTransactionAsync(BiometricFeatureType.FaceDetect, reqId, requestTimestamp, requestPayloadJson, "{}", ex.StatusCode.ToString(), "GRPC");
                return StatusCode(500, new ApiErrorResponse { Code = 500, Error = "Face detection service is unavailable." });
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Detect received invalid base64 data");
                return StatusCode(400, new ApiErrorResponse { Code = 400, Error = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidBase64 });
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "Detect received an unrecognized or malformed image");
                return StatusCode(400, new ApiErrorResponse { Code = 400, Error = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidImage });
            }
        }
        #endregion


        #region For PeopleDetection API
        /// <summary>
        /// detect people in image
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [MapToApiVersion("3.0")]
        [HttpPost("PeopleDetection", Name = "PeopleDetection")]
        [APIAuthorizationFilter]
        public async Task<ActionResult<DetectPeopleResponse>> PeopleDetection([FromBody] DetectPeopleRequest model)
        {
            DetectPeopleResponse peopleDetectResponse = new DetectPeopleResponse();
            string peopleDetectionURL = _configuration["PeopleDetectionURL"];

            //peopleDetectResponse.ErrorCode = 404;
            //peopleDetectResponse.ErrorMessage = "PeopleDetection API is not available";
            //return await ReturnResponse(peopleDetectResponse).ConfigureAwait(true);

            if (model == null || (model != null && string.IsNullOrWhiteSpace(model.encoded_image)))
            {
                peopleDetectResponse.ErrorCode = 400;
                peopleDetectResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
            }
            else if (model.encoded_image != null && model.encoded_image.Length > 0)
            {
                using (HttpClient client = new HttpClient())
                {
                    // Serialize model to JSON
                    string jsonModel = JsonConvert.SerializeObject(model);

                    // Convert the JSON data to StringContent
                    StringContent content = new StringContent(jsonModel, System.Text.Encoding.UTF8, "application/json");

                    // Send a POST request to the API endpoint with the JSON data
                    HttpResponseMessage response = await client.PostAsync(peopleDetectionURL, content).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        // Read the response content as a string
                        string responseData = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var responseModels = JsonConvert.DeserializeObject<DetectPeopleResponse>(responseData);
                        if (responseData.Contains("error"))
                        {
                            throw new Exception(responseModels.ErrorMessage);
                        }
                        if (!model.returnImage)
                        {
                            peopleDetectResponse.peopleCount = responseModels.peopleCount;
                            peopleDetectResponse.score = responseModels.score;
                            return peopleDetectResponse;
                        }
                        else
                        {
                            peopleDetectResponse = responseModels;
                            return peopleDetectResponse;
                        }
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
            }
            return await ReturnResponse(peopleDetectResponse).ConfigureAwait(true);
        }

        #endregion

        #region For CrowdDetection API
        /// <summary>
        /// detect people in image
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [MapToApiVersion("3.0")]
        [HttpPost("CrowdDetection", Name = "CrowdDetection")]
        [APIAuthorizationFilter]
        public async Task<ActionResult<CrowdDetectionResponse>> CrowdDetection([FromBody] CrowdDetectionRequest model)
        {
            CrowdDetectionResponse crowdDetectResponse = new CrowdDetectionResponse();
            string crowdDetectionURL = _configuration["CrowdDetectionURL"];
            //peopleDetectResponse.ErrorCode = 404;
            //peopleDetectResponse.ErrorMessage = "PeopleDetection API is not available";
            //return await ReturnResponse(peopleDetectResponse).ConfigureAwait(true);

            if (model == null || (model != null && string.IsNullOrWhiteSpace(model.encoded_image)))
            {
                crowdDetectResponse.ErrorCode = 400;
                crowdDetectResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
            }
            else if (model.encoded_image != null && model.encoded_image.Length > 0)
            {
                using (HttpClient client = new HttpClient())
                {
                    // Serialize model to JSON
                    string jsonModel = JsonConvert.SerializeObject(model);

                    // Convert the JSON data to StringContent
                    StringContent content = new StringContent(jsonModel, System.Text.Encoding.UTF8, "application/json");

                    // Send a POST request to the API endpoint with the JSON data
                    HttpResponseMessage response = await client.PostAsync(crowdDetectionURL, content).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        // Read the response content as a string
                        string responseData = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var responseModels = JsonConvert.DeserializeObject<CrowdDetectionResponse>(responseData);
                        if (responseData.Contains("error"))
                        {
                            throw new Exception(responseModels.ErrorMessage);
                        }
                        if (!model.returnImage)
                        {
                            crowdDetectResponse.peopleCount = responseModels.peopleCount;
                            crowdDetectResponse.score = responseModels.score;
                            return crowdDetectResponse;
                        }
                        else
                        {
                            crowdDetectResponse = responseModels;
                            return crowdDetectResponse;
                        }
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
            }

            return await ReturnResponse(crowdDetectResponse).ConfigureAwait(true);
        }
        #endregion

        #region Face API Landmark

        /// <summary>
        /// get eye/nose/mouth landmark points for the face(s) in the image
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [MapToApiVersion("3.0")]
        [HttpPost("landmark", Name = "landmark")]
        [APIAuthorizationFilter]
        public async Task<ActionResult<FaceLandmarkResponse>> Landmark([FromBody] DetectFace model)
        {
            FaceLandmarkResponse faceLandmarkResponse = new FaceLandmarkResponse();

            if (model == null || string.IsNullOrWhiteSpace(model.encoded_image))
            {
                faceLandmarkResponse.ErrorCode = 400;
                faceLandmarkResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
                return await ReturnResponse(faceLandmarkResponse).ConfigureAwait(true);
            }

            var requestTimestamp = DateTime.UtcNow;
            var reqId = Guid.NewGuid().ToString();
            var requestPayloadJson = string.Empty;

            try
            {
                // Same BioAnalyze RPC as Quality/Liveness/Analytics/Emotion/Detect, requesting
                // Detect (to localize the face) + Landmark for eye/nose/mouth points.
                var (format, width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(model.encoded_image);
                var imageBytes = Convert.FromBase64String(model.encoded_image);

                var analyzeImage = new AnalyzeImage
                {
                    Data = ByteString.CopyFrom(imageBytes),
                    Format = MapBioFormatCode(format),
                    Position = FacePosition,
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
                            Modality = FaceModality,
                            Images = { analyzeImage },
                            Features = new FeatureFlags { Detect = true, Landmark = true, Quality = true, Crop = model.GetCroppedFace }
                        }
                    }
                };
                requestPayloadJson = JsonFormatter.Default.Format(bioAnalyzeRequest);

                var grpcResponse = await _clientApiClient.BioAnalyzeAsync(bioAnalyzeRequest);
                var responseJson = JsonFormatter.Default.Format(grpcResponse);

                if (grpcResponse.Ec != 0)
                {
                    _logger.LogError("BioAnalyze (Landmark) rejected: ec={Ec} em={Em}", grpcResponse.Ec, grpcResponse.Em);
                    faceLandmarkResponse.ErrorCode = 500;
                    faceLandmarkResponse.ErrorMessage = grpcResponse.Em;
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceLandmark, reqId, requestTimestamp, requestPayloadJson, responseJson, grpcResponse.Ec.ToString(), "MASTER");
                    return await ReturnResponse(faceLandmarkResponse).ConfigureAwait(true);
                }

                var modalityResult = grpcResponse.Results.FirstOrDefault();
                if (modalityResult == null || modalityResult.Ec != 0)
                {
                    _logger.LogError("BioAnalyze (Landmark) returned a modality-level error (ec={Ec})", modalityResult?.Ec);
                    faceLandmarkResponse.ErrorCode = 500;
                    faceLandmarkResponse.ErrorMessage = "Face landmark detection could not be determined.";
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceLandmark, reqId, requestTimestamp, requestPayloadJson, responseJson, modalityResult?.Ec.ToString() ?? "MODALITY", "MASTER");
                    return await ReturnResponse(faceLandmarkResponse).ConfigureAwait(true);
                }

                // No face found is a valid empty result for a detection endpoint, not an error.
                // f.Rect is a protobuf message-type field — null when the master doesn't populate
                // it (e.g. a degenerate/non-real-face image), not just a default struct.
                // Points = the 4 corners of the bounding box (matches the old Neurotec-based API's
                // actual contract — NOT eye/nose/mouth points, despite the action's name). The real
                // named landmarks go in FaceLandmark below.
                faceLandmarkResponse.Faces = modalityResult.Faces.Select(f => new Landmarks
                {
                    Quality = f.Quality,
                    FaceRectangle = f.Rect == null ? null : new FaceRectangle { x = f.Rect.X, y = f.Rect.Y, width = f.Rect.W, height = f.Rect.H },
                    Points = f.Rect == null ? new List<Points>() : new List<Points>
                    {
                        new Points { X = f.Rect.X, Y = f.Rect.Y },
                        new Points { X = f.Rect.X + f.Rect.W, Y = f.Rect.Y },
                        new Points { X = f.Rect.X + f.Rect.W, Y = f.Rect.Y + f.Rect.H },
                        new Points { X = f.Rect.X, Y = f.Rect.Y + f.Rect.H }
                    },
                    croppedFace = model.GetCroppedFace && f.CroppedFace.Length > 0
                        ? Convert.ToBase64String(f.CroppedFace.ToByteArray())
                        : null,
                    // Confirmed live from a real master response: landmarks come back named
                    // "leftEye"/"rightEye"/"noseTip"/"mouthCenter" — no mouth-corner points exist
                    // in the master's landmark set at all (only these 4), so MouthLeft/MouthRight
                    // (never actually populated even by the old Neurotec code) aren't derivable here.
                    FaceLandmark = BuildFaceLandmark(f.Landmarks)
                }).ToList();
                faceLandmarkResponse.Code = 200;
                await LogFaceTransactionAsync(BiometricFeatureType.FaceLandmark, reqId, requestTimestamp, requestPayloadJson, responseJson, string.Empty, string.Empty);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC BioAnalyze (Landmark) call failed");
                faceLandmarkResponse.ErrorCode = 500;
                faceLandmarkResponse.ErrorMessage = "Face landmark service is unavailable.";
                await LogFaceTransactionAsync(BiometricFeatureType.FaceLandmark, reqId, requestTimestamp, requestPayloadJson, "{}", ex.StatusCode.ToString(), "GRPC");
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Landmark received invalid base64 data");
                faceLandmarkResponse.ErrorCode = 400;
                faceLandmarkResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidBase64;
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "Landmark received an unrecognized or malformed image");
                faceLandmarkResponse.ErrorCode = 400;
                faceLandmarkResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidImage;
            }
            return await ReturnResponse(faceLandmarkResponse).ConfigureAwait(true);
        }
        #endregion


        #region Face API Verify
        /// <summary>
        /// Verify two faces in two images
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>      
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [MapToApiVersion("3.0")]
        [HttpPost("verify", Name = "verify")]
        [APIAuthorizationFilter]
        public async Task<ActionResult<MatchedFaceResponse>> Verify([FromBody] VerifyFaces model)
        {
            var response = new MatchedFaceResponse();

            if (model == null || string.IsNullOrEmpty(model.encoded_image1) || string.IsNullOrEmpty(model.encoded_image2))
            {
                response.ErrorCode = BiometricResponseCode.BadRequest;
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
                return await ReturnResponse(response);
            }

            try
            {
                var (format1, width1, height1) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(model.encoded_image1);
                var (format2, width2, height2) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(model.encoded_image2);

                var masterPayload = new FaceVerifyMasterPayload
                {
                    Probe = new FaceProbeGalleryPayload
                    {
                        Faces = new FingerprintsPayload
                        {
                            BioData = new List<Models.BioData> { new Models.BioData { Format = format1, Version = ImageBioDataVersion, Wd = width1, Ht = height1, Data = model.encoded_image1 } }
                        }
                    },
                    Gallery = new FaceProbeGalleryPayload
                    {
                        Faces = new FingerprintsPayload
                        {
                            BioData = new List<Models.BioData> { new Models.BioData { Format = format2, Version = ImageBioDataVersion, Wd = width2, Ht = height2, Data = model.encoded_image2 } }
                        }
                    }
                };

                var result = await CallAsync<MatchedFaceResponse>(
                    VerifyMode, masterPayload, (r, o) => _clientApiClient.MatchAsync(r, o).ResponseAsync, nameof(Verify),
                    BiometricFeatureType.FaceVerify);

                // ABIS Match only returns the match score — faceRectangle/quality per image come
                // from a separate BioAnalyze(Detect+Quality) call each, same as the Analytics
                // endpoint above. Run both concurrently since they're independent of each other.
                if (result.MatchedFaces?.Count > 0)
                {
                    var image1FaceTask = AnalyzeFaceForVerifyAsync(model.encoded_image1);
                    var image2FaceTask = AnalyzeFaceForVerifyAsync(model.encoded_image2);
                    await Task.WhenAll(image1FaceTask, image2FaceTask);

                    result.MatchedFaces[0].image1_face = image1FaceTask.Result;
                    result.MatchedFaces[0].image2_face = image2FaceTask.Result;
                }

                return await ReturnResponse(result);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "{Operation} received invalid base64 data", nameof(Verify));
                return await ReturnResponse(new MatchedFaceResponse
                {
                    ErrorCode = BiometricResponseCode.BadRequest,
                    Message = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidBase64
                });
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "{Operation} received an unrecognized or malformed image", nameof(Verify));
                return await ReturnResponse(new MatchedFaceResponse
                {
                    ErrorCode = BiometricResponseCode.BadRequest,
                    Message = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidImage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Verify));
                return await ReturnResponse(new MatchedFaceResponse
                {
                    ErrorCode = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.GeneralErrorMessage
                });
            }
        }

        // Runs the same Detect+Quality BioAnalyze combination the Analytics endpoint (above) uses,
        // for a single image — used by Verify to fill in each side's image#_face (faceRectangle +
        // quality) alongside the Match result. Returns null rather than throwing when the master
        // finds no face on that side, since a failed per-side detection shouldn't abort the whole
        // Verify response (the match result itself is still valid).
        private async Task<Imageface> AnalyzeFaceForVerifyAsync(string encodedImage)
        {
            var requestTimestamp = DateTime.UtcNow;
            var reqId = Guid.NewGuid().ToString();
            var requestPayloadJson = string.Empty;

            try
            {
                var (format, width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(encodedImage);
                var imageBytes = Convert.FromBase64String(encodedImage);

                var analyzeImage = new AnalyzeImage
                {
                    Data = ByteString.CopyFrom(imageBytes),
                    Format = MapBioFormatCode(format),
                    Position = FacePosition,
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
                            Modality = FaceModality,
                            Images = { analyzeImage },
                            Features = new FeatureFlags { Detect = true, Quality = true }
                        }
                    }
                };
                requestPayloadJson = JsonFormatter.Default.Format(bioAnalyzeRequest);

                var grpcResponse = await _clientApiClient.BioAnalyzeAsync(bioAnalyzeRequest);
                var responseJson = JsonFormatter.Default.Format(grpcResponse);

                var modalityResult = grpcResponse.Results.FirstOrDefault();
                if (grpcResponse.Ec != 0 || modalityResult == null || modalityResult.Ec != 0 || modalityResult.Faces.Count == 0)
                {
                    _logger.LogError("BioAnalyze (Verify side-detect) found no usable face (ec={Ec})", modalityResult?.Ec ?? grpcResponse.Ec);
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceAnalytics, reqId, requestTimestamp, requestPayloadJson, responseJson,
                        (modalityResult?.Ec ?? grpcResponse.Ec).ToString(), "MASTER");
                    return null;
                }

                var face = modalityResult.Faces[0];
                await LogFaceTransactionAsync(BiometricFeatureType.FaceAnalytics, reqId, requestTimestamp, requestPayloadJson, responseJson, string.Empty, string.Empty);
                return new Imageface
                {
                    faceRectangle = face.Rect == null ? null : new FaceRectangle { x = face.Rect.X, y = face.Rect.Y, width = face.Rect.W, height = face.Rect.H },
                    quality = face.Quality
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AnalyzeFaceForVerifyAsync failed while enriching Verify response");
                return null;
            }
        }
        #endregion

        public static int MatchingThresholdFromString(double value)
        {
            double p = Math.Log10(Math.Max(double.Epsilon, Math.Min(1, value / 100)));
            return Math.Max(0, (int)Math.Round(-12 * p));
        }


        //[ApiExplorerSettings(GroupName = "Face API V3")]
        //[MapToApiVersion("3.0")]
        //[HttpPost("GetRequest", Name = "GetRequest")]
        //[APIAuthorizationFilter]
        //public async Task<IActionResult> GetRequest(IFormFile image1, IFormFile image2)
        //{
        //    if (image2 == null)
        //    {
        //        byte[] image;
        //        using (var memoryStream = new MemoryStream())
        //        {
        //            image1.CopyTo(memoryStream);
        //            image = memoryStream.ToArray();
        //        }
        //        return Ok(new DetectFace
        //        {
        //            encoded_image = Convert.ToBase64String(image)
        //        });
        //    }
        //    else
        //    {
        //        byte[] imgByte1;
        //        using (var memoryStream = new MemoryStream())
        //        {
        //            image1.CopyTo(memoryStream);
        //            imgByte1 = memoryStream.ToArray();
        //        }
        //        byte[] imgByte2;
        //        using (var memoryStream = new MemoryStream())
        //        {
        //            image2.CopyTo(memoryStream);
        //            imgByte2 = memoryStream.ToArray();
        //        }
        //        return Ok(new VerifyFaces
        //        {
        //            encoded_image1 = Convert.ToBase64String(imgByte1),
        //            encoded_image2 = Convert.ToBase64String(imgByte2)
        //        });
        //    }
        //}

        /// <summary>
        /// Create a zip in stream file from base64 images
        /// </summary>
        /// <param name="encoded_Image"></param>
        /// <returns></returns>
        public static MemoryStream ZipGenerator(string encoded_Image)
        {
            ZipArchiveEntry fileInArchive;
            Stream entryStream;
            int i = 0;
            List<byte[]> byteArray = new List<byte[]>();
            List<string> files = Enumerable.Repeat(encoded_Image, 4).ToList();
            foreach (var file in files)
            {
                byteArray.Add(Convert.FromBase64String(file));
            }

            var outStream = new MemoryStream();

            using (var archive = new ZipArchive(outStream, ZipArchiveMode.Create, true))
            {
                foreach (var file in files)
                {
                    fileInArchive = (archive.CreateEntry((i + 1) + ".jpeg", CompressionLevel.Optimal));
                    using (entryStream = fileInArchive.Open())
                    {
                        using (var fileToCompressStream = new MemoryStream(byteArray[i]))
                        {
                            fileToCompressStream.CopyTo(entryStream);
                        }
                        i++;
                    }
                }
            }
            outStream.Position = 0;
            return outStream;
        }
        [MapToApiVersion("3.0")]
        [HttpPost("Liveness", Name = "Liveness")]
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [APIAuthorizationFilter]
        public async Task<ActionResult<MxfaceWebAPI.Models.LivenessResponse>> Liveness([FromBody] DetectLiveness model)
        {
            MxfaceWebAPI.Models.LivenessResponse response = new MxfaceWebAPI.Models.LivenessResponse();
            if (!_IsEnabledPassiveLiveness)
            {
                return StatusCode(403, MxfaceWebAPI.Models.Response.ApiErrorEnvelope.Create(403, "Liveness API currently not available."));
            }

            if (model == null || string.IsNullOrWhiteSpace(model.encoded_image))
            {
                return StatusCode(400, MxfaceWebAPI.Models.Response.ApiErrorEnvelope.Create(400, global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest));
            }

            var requestTimestamp = DateTime.UtcNow;
            var reqId = Guid.NewGuid().ToString();
            var requestPayloadJson = string.Empty;

            try
            {
                // Same BioAnalyze RPC as Quality (see that action's comment for why — it's a
                // directly-typed RPC, not the generic ClientApiRequest envelope), with
                // FeatureFlags.Liveness instead of Quality.
                var (format, width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(model.encoded_image);
                var imageBytes = Convert.FromBase64String(model.encoded_image);

                var analyzeImage = new AnalyzeImage
                {
                    Data = ByteString.CopyFrom(imageBytes),
                    Format = MapBioFormatCode(format),
                    Position = FacePosition,
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
                            Modality = FaceModality,
                            Images = { analyzeImage },
                            Features = new FeatureFlags { Liveness = true }
                        }
                    }
                };
                requestPayloadJson = JsonFormatter.Default.Format(bioAnalyzeRequest);

                var grpcResponse = await _clientApiClient.BioAnalyzeAsync(bioAnalyzeRequest);
                var responseJson = JsonFormatter.Default.Format(grpcResponse);

                if (grpcResponse.Ec != 0)
                {
                    _logger.LogError("BioAnalyze (Liveness) rejected: ec={Ec} em={Em}", grpcResponse.Ec, grpcResponse.Em);
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceLiveness, reqId, requestTimestamp, requestPayloadJson, responseJson, grpcResponse.Ec.ToString(), "MASTER");
                    return StatusCode(500, MxfaceWebAPI.Models.Response.ApiErrorEnvelope.Create(500, grpcResponse.Em));
                }

                var modalityResult = grpcResponse.Results.FirstOrDefault();
                if (modalityResult == null || modalityResult.Ec != 0 || modalityResult.Faces.Count == 0)
                {
                    _logger.LogError("BioAnalyze (Liveness) returned no usable face result (modality ec={Ec})", modalityResult?.Ec);
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceLiveness, reqId, requestTimestamp, requestPayloadJson, responseJson, modalityResult?.Ec.ToString() ?? "MODALITY", "MASTER");
                    return StatusCode(500, MxfaceWebAPI.Models.Response.ApiErrorEnvelope.Create(500, "Face liveness could not be determined."));
                }

                var faceResult = modalityResult.Faces[0];

                if (faceResult.Liveness == null)
                {
                    _logger.LogError("BioAnalyze (Liveness) returned a face with no Liveness result populated");
                    await LogFaceTransactionAsync(BiometricFeatureType.FaceLiveness, reqId, requestTimestamp, requestPayloadJson, responseJson, "NO_LIVENESS", "MASTER");
                    return StatusCode(500, MxfaceWebAPI.Models.Response.ApiErrorEnvelope.Create(500, "Face liveness could not be determined."));
                }

                // TODO: ALiveness.Score (extract.proto) is an unscaled int32 — its 0-100 vs 0-1
                // convention isn't documented anywhere in the proto. Assuming 0-100 (dividing by
                // 100 to produce a 0-1 LivenessScore, e.g. a raw score of 94 -> 0.94) until
                // confirmed with the ABIS/master team — same flagged-placeholder treatment as
                // BioFormat/BioPosition above (see CLAUDE.md "Known placeholder/unconfirmed values").
                response.LivenessScore = faceResult.Liveness.Score;
                //response.LivenessScore = faceResult.Liveness.Score / 100f;
                await LogFaceTransactionAsync(BiometricFeatureType.FaceLiveness, reqId, requestTimestamp, requestPayloadJson, responseJson, string.Empty, string.Empty);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC BioAnalyze (Liveness) call failed");
                await LogFaceTransactionAsync(BiometricFeatureType.FaceLiveness, reqId, requestTimestamp, requestPayloadJson, "{}", ex.StatusCode.ToString(), "GRPC");
                return StatusCode(500, MxfaceWebAPI.Models.Response.ApiErrorEnvelope.Create(500, "Face liveness service is unavailable."));
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Liveness received invalid base64 data");
                return StatusCode(400, MxfaceWebAPI.Models.Response.ApiErrorEnvelope.Create(400, global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidBase64));
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "Liveness received an unrecognized or malformed image");
                return StatusCode(400, MxfaceWebAPI.Models.Response.ApiErrorEnvelope.Create(400, global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidImage));
            }
            return Ok(response);
        }




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

        private static string Truncate(string value, int maxLength) =>
           value.Length <= maxLength ? value : value[..maxLength];

        // A failed audit-log write must never turn a real (successful or already-failed)
        // biometric call into a different outcome for the caller — swallow and log only. Same
        // convention as BiometricControllerBase.LogTransactionSafeAsync.
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

        // Shared by every BioAnalyze-based action (Quality/Liveness/Analytics/Emotion/Detect) —
        // each one only supplies its own featureType/reqId/requestTimestamp (captured at the top of
        // the action, since reqId also needs to go into the outgoing BioAnalyzeRequest) plus the
        // per-call request/response payload and error info. Replaces what used to be a separate
        // local function duplicated in each action.
        private async Task LogFaceTransactionAsync(
            int featureType, string reqId, DateTime requestTimestamp,
            string requestPayload, string responsePayload, string error, string errorPoint)
        {
            var clientId = ResolvedClientId ?? 0;
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            await LogTransactionSafeAsync(new TransactionLogEntry
            {
                ClientId = clientId,
                ReqId = Truncate(reqId, 36),
                RequestPayload = requestPayload,
                RequestTimestamp = requestTimestamp,
                ResponsePayload = responsePayload,
                Error = Truncate(error, 10),
                ResponseTimestamp = DateTime.UtcNow,
                ErrorPoint = errorPoint,
                FeatureType = featureType,
                QuotaCount = 1,
                ClientIp = Truncate(clientIp, 50),
                CreatedBy = clientId
            });
        }

        private static int MapBioFormatCode(string format) => format switch
        {
            "BMP" => BioFormatBmp,
            "JPEG" => BioFormatJpeg,
            "PNG" => BioFormatPng,
            _ => throw new InvalidDataException($"Unsupported image format for BioAnalyze: {format}")
        };

        // Landmark name strings confirmed live from a real master response (2026-09-24):
        // "leftEye"/"rightEye"/"noseTip"/"mouthCenter" — the master's landmark set has no other
        // named points (no mouth corners), so FaceLandmark can only ever populate these 4.
        private static FaceLandmark BuildFaceLandmark(IEnumerable<Grpc.Extract.ALandmark> landmarks)
        {
            var landmarkList = landmarks.ToList();
            PointLocation? Find(string name) =>
                landmarkList.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) is { } match
                    ? new PointLocation { X = match.X, Y = match.Y }
                    : null;

            return new FaceLandmark
            {
                eye_left = Find("leftEye"),
                eye_right = Find("rightEye"),
                Nose = Find("noseTip"),
                MouthCenter = Find("mouthCenter"),
                FeaturePoints = landmarkList.Select(l => new PointLocation { X = l.X, Y = l.Y }).ToList()
            };
        }
    }
}
