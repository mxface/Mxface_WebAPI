using Asp.Versioning;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MxfaceWebAPI.Common;
using MxfaceWebAPI.Filters;
using MxfaceWebAPI.Grpc.AbisClient;
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
    public class FaceController : ControllerBase
    {
        // TODO: confirm the real "mode" value the master uses for a quality-only Match call.
        private const int QualityCheckMode = 0;

        private readonly ClientApiService.ClientApiServiceClient _clientApiClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FaceController> _logger;        
        public static bool _IsEnabledPassiveLiveness = true;

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
                    Data = System.Text.Json.JsonSerializer.Serialize(request)
                };

                var grpcResponse = await _clientApiClient.MatchAsync(grpcRequest);

                response.Result = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(grpcResponse.ResponseJson);
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

        #region Face API Landmark-- From NEURO

        /// <summary>
        /// get landmark from face (emotion, age, gender)-- From Neuro
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [ApiExplorerSettings(GroupName = "Face API V3")]
        [MapToApiVersion("3.0")]
        [HttpPost("landmark", Name = "landmark")]
        [APIAuthorizationFilter]
        public async Task<ActionResult<FaceLandmarkResponse>> Landmark([FromBody] DetectFace model)
        {
            int status_code = 200;
            string message = string.Empty;
            FaceLandmarkResponse faceDetectResponse = new FaceLandmarkResponse();
            try
            {
                if (model == null || (model != null && string.IsNullOrWhiteSpace(model.encoded_image)))
                {
                    faceDetectResponse.ErrorCode = 400;
                    faceDetectResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
                }
                else if (model.encoded_image != null && model.encoded_image.Length > 0)
                {
                    //faceDetectResponse = NeuroBiometric.Landmark(model);
                }
            }
            catch (Exception ex)
            {               
            }
            return await ReturnResponse(faceDetectResponse).ConfigureAwait(true);
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
            float Quality = string.IsNullOrEmpty(_configuration["MXFaceQuality"]) ?
                float.Parse("0.6") : float.Parse(_configuration["MXFaceQuality"]);
            int MatchedConfidence = string.IsNullOrEmpty(_configuration["MatchedConfidence"]) ? 60 : Int32.Parse(_configuration["MatchedConfidence"]);

            MatchedFaceResponse matchedFaceResponse = new MatchedFaceResponse();

            if (model == null ||
                (model != null && model.encoded_image1 == null) ||
                (model != null && model.encoded_image1 != null && model.encoded_image1.Length <= 0) ||
                (model != null && model.encoded_image2 == null) ||
                (model != null &&
                 model.encoded_image2 != null && model.encoded_image2.Length <= 0))
            {
                matchedFaceResponse.ErrorCode = 400;
                matchedFaceResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
            }
            else if (model.encoded_image1 != null && model.encoded_image1.Length > 0
                && model.encoded_image2 != null && model.encoded_image2.Length > 0)
            {
                try
                {
                    if (model.QualityThreshold.HasValue && (model.QualityThreshold > 0 && model.QualityThreshold < 20 || model.QualityThreshold <= 0))
                    {
                        matchedFaceResponse.ErrorCode = 400;
                        matchedFaceResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.ClientQualityThersholdMsg;
                        return await ReturnResponse(matchedFaceResponse).ConfigureAwait(true);
                    }
                    else
                    {
                        Quality = model.QualityThreshold.HasValue ? (model.QualityThreshold.Value) : Quality;
                    }
                    if (!model.FAR.HasValue)
                    {
                        model.FAR = 0.01;
                    }

                    int matchingThreshold = MatchingThresholdFromString(model.FAR.Value);
                    bool isValid = false;
                    switch (matchingThreshold)
                    {
                        case 0:
                            isValid = true;
                            break;
                        case 12:
                            isValid = true;
                            break;
                        case 24:
                            isValid = true;
                            break;
                        case 36:
                            isValid = true;
                            break;
                        case 48:
                            isValid = true;
                            break;
                        case 60:
                            isValid = true;
                            break;
                        case 72:
                            isValid = true;
                            break;
                        case 84:
                            isValid = true;
                            break;
                        case 96:
                            isValid = true;
                            break;
                    }
                    if (isValid == false)
                    {
                        matchedFaceResponse.ErrorCode = 400;
                        matchedFaceResponse.ErrorMessage = "Invalid request param FAR";
                        return await ReturnResponse(matchedFaceResponse).ConfigureAwait(true);
                    }
                    return null;
                    //NeuroBiometric.VerifyMulti(model, 1, Quality, matchingThreshold);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Face Verify V3");                   
                    matchedFaceResponse.ErrorCode = 500;
                    matchedFaceResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.GeneralErrorMessage;
                }
            }
            else
            {
                matchedFaceResponse.ErrorCode = 400;
                matchedFaceResponse.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
            }
            return await ReturnResponse(matchedFaceResponse).ConfigureAwait(true);         
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
                response.ErrorCode = 403;
                response.ErrorMessage = "Liveness API currently not available.";
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            try
            {
                if (model == null || (model != null && string.IsNullOrWhiteSpace(model.encoded_image)))
                {
                    response.ErrorCode = 400;
                    response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
                }
                else if (model.encoded_image != null && model.encoded_image.Length > 0)
                {
                    //response = NeuroBiometric.NeuroDetectLivenessAsync(model.encoded_image);
                    response = null;
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("The input is not a valid Base-64 string") || ex.Message.Contains("NStream read failed"))
                {
                    response.ErrorCode = 400;
                    response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidBase64;
                }
                else if (ex.Message.Contains("Image cannot be loaded") || ex.Message.Contains("No image format found that supports the specified stream"))
                {
                    response.ErrorCode = 400;
                    response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidImage;
                }
                else
                {
                    _logger.LogError(ex, "Liveness V3");
                    //await _emailService.ExceptionMailSend("V3 Liveness", ex).ConfigureAwait(true);
                    response.ErrorCode = 500;
                    response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.GeneralErrorMessage;
                }
            }
            return await ReturnResponse(response).ConfigureAwait(true);
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
    }
}
