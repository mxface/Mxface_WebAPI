using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MxfaceWebAPI.Common;
using MxfaceWebAPI.Data;
using MxfaceWebAPI.Filters;
using MxfaceWebAPI.Grpc;
using MxfaceWebAPI.Grpc.AbisClient;
using MxfaceWebAPI.Models;
using MxfaceWebAPI.Models.Request.FaceIdentity;
using MxfaceWebAPI.Models.Response;
using MxfaceWebAPI.Models.Response.FaceIdentity;
using MxfaceWebAPI.Services;



namespace MxfaceWebAPI.Controllers.V3
{
    // Subscription-key auth (via [APIAuthorizationFilter] on each action) gates these endpoints,
    // not the global JWT policy — AllowAnonymous opts out of that so the filter is what runs.
    [AllowAnonymous]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("3.0")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "Identity V3")]
    public class FaceIdentityController : BiometricControllerBase
    {
        // TODO: confirm the real mode value the master uses for Face Enroll/Search.
        private const int EnrollMode = 0;
        private const int SearchMode = 0;

        // Per the official ABIS Client API v2.0 reference doc's Biometric Data Formats table
        // (§21): raster formats (BMP/JPEG/PNG/RAW/WSQ) all pair with version="0" regardless of
        // which one — only the ISO FIR/IIR/FID record formats use "2005"/"2011". Unconfirmed
        // specifically for Face — assumed to follow the same convention already confirmed live
        // for Finger/Iris.
        private const string ImageBioDataVersion = "0";

        // REST error-contract text for a missing/invalid required field — matches the legacy
        // Face API's per-field messages (see D:\LiveBranchDeployment\webapi.face reference),
        // not the generic Finger/Iris contract message, per explicit instruction to preserve the
        // existing Face API contract.


        private readonly ClientApiService.ClientApiServiceClient _clientApiClient;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;
        private readonly IGroupService _groupService;

        // Resolved by APIAuthorizationFilterAttribute and stashed in HttpContext.Items — same
        // pattern as BiometricControllerBase.ResolvedClientId, kept local here since this
        // controller doesn't inherit that base.
        private long? ResolvedClientId =>
            HttpContext.Items.TryGetValue("ClientId", out var value) && value is long clientId ? clientId : null;

        // Also resolved by APIAuthorizationFilterAttribute and stashed alongside ClientId — used
        // to build the ABIS admin/clients URL in GroupService's calls to IAbisAdminApiClient.
        private string? ResolvedClientCode =>
            HttpContext.Items.TryGetValue("ClientCode", out var value) && value is string clientCode ? clientCode : null;

        #region For Refrence Call
        public FaceIdentityController(ClientApiService.ClientApiServiceClient clientApiClient, IClientApiEnvelopeFactory envelopeFactory,
            IConfiguration configuration, IPostgresHelper postgresHelper, ILogger<FaceIdentityController> logger,
            IEmailService emailService, IGroupService groupService) : base(envelopeFactory, configuration, postgresHelper, logger)

        {
            _clientApiClient = clientApiClient;
            _emailService = emailService;
            _config = configuration;
            _groupService = groupService;
        }
        #endregion


        #region face-Enroll API
        /// <summary>
        /// Create Identity
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [MapToApiVersion("3.0")]
        [HttpPost(Name = "Create")]
        [APIAuthorizationFilter]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<FaceIdentityInfo> Enroll([FromBody] Models.Request.FaceIdentity.CreateFaceIdentityRequest model)
        {
            var response = new FaceIdentityInfo();
            float Quality = float.Parse(_config["FaceIdentityQuality"]); // MXface Defined quality
            int MatchedConfidence = string.IsNullOrEmpty(_config["MatchedConfidence"]) ? 60 : Int32.Parse(_config["MatchedConfidence"]);

            if (model == null)
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return response;
            }
            if (model.QualityThreshold.HasValue && (model.QualityThreshold > 0 && model.QualityThreshold < 20 || model.QualityThreshold <= 0))
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.ClientQualityThersholdMsg;
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return response;
            }
            if (model.GroupIds == null || !model.GroupIds.Any())
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest + " Group Id is required";
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return response;
            }
            if (string.IsNullOrWhiteSpace(model.Encoded_Image))
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest + " Encoded Image is required";
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return response;
            }
            if (string.IsNullOrWhiteSpace(model.externalId))
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest + " External id required";
                response.Code = BiometricResponseCode.BadRequest;
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return response;
            }
            try
            {
                var clientId = ResolvedClientId!.Value;
                //int clientId = GetClientID();

                // Resolve every GroupId to its real GroupName from faceclient_db.groups (never send
                // the raw numeric id to ABIS as groupName) and validate all of them belong to this
                // client BEFORE making any ABIS call — reject the whole request if even one doesn't
                // exist, rather than partially enrolling into whichever groups did resolve.
                var groups = new List<(int GroupId, string GroupName)>();
                foreach (var groupId in model.GroupIds)
                {
                    var group = await _groupService.GetGroupAsync(clientId, groupId);
                    if (group is null)
                    {
                        Response.StatusCode = BiometricResponseCode.BadRequest;
                        return new FaceIdentityInfo
                        {
                            Code = BiometricResponseCode.BadRequest,
                            ErrorMessage = $"Could not find a Group with the specified ID: {groupId}"
                        };
                    }
                    groups.Add((groupId, group.GroupName));
                }

                // Face images commonly arrive as JPEG/PNG, not just BMP like Finger/Iris test
                // captures — detect the real format instead of assuming BMP.
                var (format, width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(model.Encoded_Image);
                var bioDataEntry = new BioData { Format = format, Version = ImageBioDataVersion, Wd = width, Ht = height, Data = model.Encoded_Image };

                FaceIdentityInfo enrollResult = new();

                // The master's groupName field only takes one group per call, so multiple GroupIds
                // run the duplicate-check Search + Enroll pair once per group, sequentially. A
                // referenceId already enrolled from an earlier group in this loop makes the next
                // group's Enroll call fail as "already enrolled" — CallAsync's existing
                // shouldRetryWithFallback/Update retry (below) picks that up automatically and
                // attaches this group to the existing identity instead of surfacing the rejection.
                foreach (var (groupId, groupName) in groups)
                {
                    // Reference D:\LiveBranchDeployment\webapi.face FaceIdentityController.cs lines
                    // 192-250: before enrolling, search the target group for a similar existing
                    // identity — reject unless the caller explicitly opts in via ForceAdd.
                    if (!model.ForceAdd)
                    {
                        var searchPayload = new FaceSearchMasterPayload
                        {
                            GroupName = groupName,
                            Faces = new FingerprintsPayload { BioData = new List<BioData> { bioDataEntry } }
                        };

                        var searchResult = await CallAsync<BiometricSearchResponse>(
                            SearchMode, searchPayload, (r, o) => _clientApiClient.IdentifyAsync(r, o).ResponseAsync, nameof(Enroll) + "->Search",
                            BiometricFeatureType.FaceEnroll);

                        // MatchResult is only null when the search call itself failed (technical/master
                        // rejection) — CallAsync's success path always sets it to a (possibly empty)
                        // list. Don't silently fall through to Enroll on a failed pre-check; surface it.
                        if (searchResult.MatchResult == null)
                        {
                            return new FaceIdentityInfo
                            {
                                Code = searchResult.Code,
                                Message = searchResult.Message,
                                ErrorMessage = searchResult.ErrorMessage
                            };
                        }

                        if (searchResult.MatchResult.Any(m => m.MatchingScore.HasValue && m.MatchingScore.Value > MatchedConfidence))
                        {
                            _logger.LogInformation("{Operation}: rejecting enroll, a similar identity already exists in group {GroupName} (ForceAdd=false)", nameof(Enroll), groupName);
                            Response.StatusCode = BiometricResponseCode.BadRequest;
                            return new FaceIdentityInfo
                            {
                                Code = BiometricResponseCode.BadRequest,
                                ErrorMessage = "Identity is similar to an existing identities."
                            };
                        }
                    }

                    var masterPayload = new FaceEnrollMasterPayload
                    {
                        GroupName = groupName,
                        Demographics = new DemographicsPayload { ReferenceId = model.externalId },
                        Faces = new FingerprintsPayload { BioData = new List<BioData> { bioDataEntry } }
                    };

                    enrollResult = await CallAsync<FaceIdentityInfo>(
                        EnrollMode, masterPayload, (r, o) => _clientApiClient.EnrolAsync(r, o).ResponseAsync, nameof(Enroll),
                        BiometricFeatureType.FaceEnroll,
                        // referenceId already has an identity (e.g. enrolled via a different modality,
                        // or an earlier group in this same loop) — retry via Update instead of
                        // surfacing the rejection, same as FingerPrint/Iris Enroll.
                        shouldRetryWithFallback: MasterErrorMapper.IsAlreadyEnrolledError,
                        fallbackGrpcCall: (r, o) => _clientApiClient.UpdateAsync(r, o).ResponseAsync);

                    if (enrollResult.Code != BiometricResponseCode.Success)
                    {
                        return enrollResult; // abort on the first failing group, don't attempt the rest
                    }
                }

                return enrollResult;
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "{Operation} received invalid base64 data", nameof(Enroll));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return new FaceIdentityInfo
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidBase64
                };
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "{Operation} received an unrecognized or malformed image", nameof(Enroll));
                Response.StatusCode = BiometricResponseCode.BadRequest;
                return new FaceIdentityInfo
                {
                    Code = BiometricResponseCode.BadRequest,
                    Message = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidImage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Enroll));
                await _emailService.ExceptionMailSend(nameof(Enroll), ex).ConfigureAwait(true);
                Response.StatusCode = BiometricResponseCode.ServiceUnavailable;
                return new FaceIdentityInfo
                {
                    Code = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.GeneralErrorMessage
                };
            }
        }
        #endregion

        #region face-Search API
        [HttpPost]
        [Route("search", Name = "search")]
        [APIAuthorizationFilter]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<SearchFaceIdentityResponse>> Search([FromBody] Models.Request.FaceIdentity.SearchFaceIdentity request)
        {
            SearchFaceIdentityResponse response = new SearchFaceIdentityResponse();
            int MatchedConfidence = string.IsNullOrEmpty(_config["MatchedConfidence"]) ? 60 : Int32.Parse(_config["MatchedConfidence"]);

            if (request == null)
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
                response.ErrorCode = BiometricResponseCode.BadRequest;
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            int limit = request.Limit ?? 1;
            if (limit <= 0)
            {
                response.ErrorMessage = "Limit should be greater than 0";
                response.ErrorCode = BiometricResponseCode.BadRequest;
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            if (limit > 10)
            {
                response.ErrorMessage = "Limit should be less than 10";
                response.ErrorCode = BiometricResponseCode.BadRequest;
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            if (string.IsNullOrWhiteSpace(request.Encoded_Image))
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest + " Encoded Image is required.";
                response.ErrorCode = BiometricResponseCode.BadRequest;
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            if (request.GroupIds == null || !request.GroupIds.Any())
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest + " Group Id is required";
                response.ErrorCode = BiometricResponseCode.BadRequest;
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            // Validated for contract compatibility only — the master's Identify payload has no
            // quality-threshold field, so it isn't forwarded.
            if (request.QualityThreshold.HasValue && (request.QualityThreshold > 0 && request.QualityThreshold < 20 || request.QualityThreshold <= 0))
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.ClientQualityThersholdMsg;
                response.ErrorCode = BiometricResponseCode.BadRequest;
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            if (request.MatchConfidence.HasValue && request.MatchConfidence.Value > 0)
            {
                MatchedConfidence = request.MatchConfidence.Value;
            }

            try
            {
                var clientId = ResolvedClientId!.Value;

                // Resolve every GroupId to its real GroupName and validate all of them belong to
                // this client BEFORE making any ABIS call — same as Enroll.
                var groups = new List<(int GroupId, string GroupName)>();
                foreach (var groupId in request.GroupIds)
                {
                    var group = await _groupService.GetGroupAsync(clientId, groupId);
                    if (group is null)
                    {
                        response.ErrorMessage = $"Could not find a Group with the specified ID: {groupId}";
                        response.ErrorCode = BiometricResponseCode.BadRequest;
                        return await ReturnResponse(response).ConfigureAwait(true);
                    }
                    groups.Add((groupId, group.GroupName));
                }

                var (format, width, height) = global::MxfaceWebAPI.CommonHelper.CommonHelper.DetectImageFormat(request.Encoded_Image);
                var bioDataEntry = new BioData { Format = format, Version = ImageBioDataVersion, Wd = width, Ht = height, Data = request.Encoded_Image };

                // referenceId -> (best score across groups, groups it matched in)
                var candidates = new Dictionary<string, (float Score, List<int> GroupIds)>();

                // The master's groupName field only takes one group per call, so Identify runs
                // once per group and the candidate lists are merged.
                foreach (var (groupId, groupName) in groups)
                {
                    var searchPayload = new FaceSearchMasterPayload
                    {
                        GroupName = groupName,
                        Faces = new FingerprintsPayload { BioData = new List<BioData> { bioDataEntry } }
                    };

                    var searchResult = await CallAsync<BiometricSearchResponse>(
                        SearchMode, searchPayload, (r, o) => _clientApiClient.IdentifyAsync(r, o).ResponseAsync, nameof(Search),
                        BiometricFeatureType.FaceSearch);

                    // MatchResult is only null when the Identify call itself failed — surface it.
                    if (searchResult.MatchResult == null)
                    {
                        response.ErrorCode = searchResult.Code ?? BiometricResponseCode.ServiceUnavailable;
                        response.Message = searchResult.Message;
                        response.ErrorMessage = searchResult.ErrorMessage;
                        return await ReturnResponse(response).ConfigureAwait(true);
                    }

                    foreach (var match in searchResult.MatchResult.Where(m => !string.IsNullOrEmpty(m.ExternalId) && m.MatchingScore.HasValue))
                    {
                        if (candidates.TryGetValue(match.ExternalId!, out var existing))
                        {
                            existing.GroupIds.Add(groupId);
                            candidates[match.ExternalId!] = (Math.Max(existing.Score, match.MatchingScore!.Value), existing.GroupIds);
                        }
                        else
                        {
                            candidates[match.ExternalId!] = (match.MatchingScore!.Value, new List<int> { groupId });
                        }
                    }
                }

                var identityConfidences = candidates
                    .Where(c => c.Value.Score >= MatchedConfidence)
                    .OrderByDescending(c => c.Value.Score)
                    .Take(limit)
                    .Select(c => new IdentityConfidences
                    {
                        identity = new Identity { ExternalId = c.Key, GroupIds = c.Value.GroupIds },
                        matchResult = 1,
                        confidence = request.returnConfidence ? c.Value.Score : null
                    })
                    .ToList();

                response.SearchedIdentities = new List<LookupIdentities>
                {
                    new LookupIdentities { identityConfidences = identityConfidences }
                };
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "{Operation} received invalid base64 data", nameof(Search));
                response.ErrorCode = BiometricResponseCode.BadRequest;
                response.Message = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidBase64;
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "{Operation} received an unrecognized or malformed image", nameof(Search));
                response.ErrorCode = BiometricResponseCode.BadRequest;
                response.Message = global::MxfaceWebAPI.CommonHelper.CommonHelper.InvalidImage;
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Search));
                await _emailService.ExceptionMailSend(nameof(Search), ex).ConfigureAwait(true);
                response.ErrorCode = BiometricResponseCode.ServiceUnavailable;
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.GeneralErrorMessage;
                return await ReturnResponse(response).ConfigureAwait(true);
            }
        }
        #endregion


        #region face-AddFace API
        [HttpPost]
        [Route("{faceIdentityId}/face", Name = "AddFace")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        [APIAuthorizationFilter]
        [MapToApiVersion("3.0")]
        public async Task<ActionResult<Models.Response.FaceIdentity.FaceInfo>> AddFaceInExistingIdentity([FromRoute] int faceIdentityId, [FromBody] Models.Request.FaceIdentity.AddFaceRequest model)
        {
            int offset = -1;
            int limit = 10;
            Models.Response.FaceIdentity.FaceInfo response = new Models.Response.FaceIdentity.FaceInfo();
            float Quality = 0.5f;
            int MatchedConfidence = string.IsNullOrEmpty(_config["MatchedConfidence"]) ? 60 : Int32.Parse(_config["MatchedConfidence"]);

            return await ReturnResponse(response).ConfigureAwait(true);

        }

        #endregion


        #region
        /// <summary>
        /// Get identity by external id — stub. Not yet implemented: the legacy version looked up
        /// a local DB (_faceIdentityService), which doesn't exist in this gRPC-based project.
        /// Needs its own design pass (likely the master's Search/Identify by referenceId) before
        /// being built for real.
        /// </summary>
        /// <param name="externalId"></param>
        /// <returns></returns>
        [HttpGet]
        [APIAuthorizationFilter]
        [MapToApiVersion("3.0")]
        [Route("{externalId}/facIdentities", Name = "GetFaceByExternalId")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public Task<IActionResult> Get([FromRoute] string externalId)
        {
            return Task.FromResult<IActionResult>(StatusCode(501));
        }
        #endregion

        [HttpGet("{faceIdentityId}", Name = "GetByIdentityId")]
        [APIAuthorizationFilter]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<FaceIdentityInfo>> Get([FromRoute] int faceIdentityId)
        {
            Models.Response.FaceIdentity.FaceIdentityInfo response = new Models.Response.FaceIdentity.FaceIdentityInfo();
            return await ReturnResponse(response).ConfigureAwait(true);
        }

        [APIAuthorizationFilter]
        [HttpGet(Name = "GetIdentityByGroup")]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<Models.Response.FaceIdentity.FaceIdentityResponse>> Get(int? groupId, int offset = 0, int limit = 10)
        {
            var response = new Models.Response.FaceIdentity.FaceIdentityResponse
            {
                ErrorCode = BiometricResponseCode.NotImplemented,
                ErrorMessage = "Listing identities by group is not yet available; the ABIS master gRPC support for it is under development."
            };
            return await ReturnResponse(response).ConfigureAwait(true);
        }

        [HttpDelete("{faceIdentityId}", Name = "DeleteIdentity")]
        [APIAuthorizationFilter]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<FaceIdentityInfo>> Delete([FromRoute] int faceIdentityId)
        {
            FaceIdentityInfo response = new FaceIdentityInfo();
            try
            {

            }
            catch (Exception ex)
            {

                throw;
            }

            response.ErrorCode = BiometricResponseCode.NotImplemented;
            response.ErrorMessage = "Deleting an identity by internal id is not yet available; the ABIS master gRPC support for it is under development.";
            return await ReturnResponse(response).ConfigureAwait(true);
        }


        [HttpPost]
        [APIAuthorizationFilter]
        [Route("{faceIdentityId}/addMetadata", Name = "AddMetadata")]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<MetadataInfo>> Post([FromRoute] int faceIdentityId, [FromBody] MetadataRequest request)
        {
            if (request == null || !request.Metadata.Any())
            {
                return await ReturnResponse(new MetadataInfo { ErrorCode = StatusCodes.Status400BadRequest, ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest }).ConfigureAwait(true);
            }
            if (faceIdentityId <= 0)
            {
                return await ReturnResponse(new MetadataInfo { ErrorCode = StatusCodes.Status400BadRequest, ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest }).ConfigureAwait(true);
            }

            return await ReturnResponse(new MetadataInfo
            {
                ErrorCode = BiometricResponseCode.NotImplemented,
                ErrorMessage = "Adding identity metadata is not yet available; the ABIS master gRPC support for it is under development."
            }).ConfigureAwait(true);
        }


        [HttpPut]
        [APIAuthorizationFilter]
        [Route("{faceIdentityId}/updatemetadata", Name = "UpdateMetadata")]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<MetadataInfo>> Put([FromRoute] int faceIdentityId, [FromBody] MetadataRequest request)
        {

            if (faceIdentityId <= 0)
            {
                return await ReturnResponse(new MetadataInfo { ErrorCode = StatusCodes.Status400BadRequest, ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest }).ConfigureAwait(true);
            }

            if (request == null || !request.Metadata.Any())
            {
                return await ReturnResponse(new MetadataInfo { ErrorCode = StatusCodes.Status400BadRequest, ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest }).ConfigureAwait(true);
            }

            return await ReturnResponse(new MetadataInfo
            {
                ErrorCode = BiometricResponseCode.NotImplemented,
                ErrorMessage = "Updating identity metadata is not yet available; the ABIS master gRPC support for it is under development."
            }).ConfigureAwait(true);
        }


        [HttpPut]
        [APIAuthorizationFilter]
        [Route("{faceIdentityId}/updateGroup", Name = "UpdateIdentity")]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<FaceIdentityInfo>> Put([FromRoute] int faceIdentityId, [FromBody] Models.Request.FaceIdentity.UpdateGroupRequest request)
        {
            FaceIdentityInfo response = new FaceIdentityInfo();
            string[] addGrpIds = null;
            string[] delGrpIds = null;
            if (faceIdentityId <= 0)
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
                response.ErrorCode = 400;
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            if (request == null)
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest;
                response.ErrorCode = 400;
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            if (request.AddGroupIds == null && request.DeleteGroupIds == null)
            {
                response.ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest + " addGroupIds or deleteGroupIds must have valid group ids.";
                response.ErrorCode = 400;
                return await ReturnResponse(response).ConfigureAwait(true);
            }

            response.ErrorCode = BiometricResponseCode.NotImplemented;
            response.ErrorMessage = "Updating an identity's group memberships is not yet available; the ABIS master gRPC support for it is under development.";
            return await ReturnResponse(response).ConfigureAwait(true);
        }

        [HttpGet]
        [APIAuthorizationFilter]
        [Route("{faceIdentityId}/faces", Name = "GetFacesByIdentity")]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<GetFaceResponse>> GetFace([FromRoute] int faceIdentityId)
        {
            GetFaceResponse response = new GetFaceResponse
            {
                ErrorCode = BiometricResponseCode.NotImplemented,
                ErrorMessage = "Listing faces for an identity is not yet available; the ABIS master gRPC support for it is under development."
            };

            return await ReturnResponse(response).ConfigureAwait(true);
        }

        #region For DeleteFace
        [HttpDelete]
        [APIAuthorizationFilter]
        [Route("{faceIdentityId}/faces/{faceId}", Name = "DeleteFace")]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<Models.Response.FaceIdentity.FaceInfo>> Delete([FromRoute] int faceId, [FromRoute] int faceIdentityId)
        {
            Models.Response.FaceIdentity.FaceInfo response = new Models.Response.FaceIdentity.FaceInfo();
            try
            {
            }
            catch (Exception ex)
            {
            }

            // FaceInfo doesn't inherit BiomatricBaseResponse (no ErrorCode field), so ReturnResponse
            // can't key off it here — return the not-implemented status directly.
            return StatusCode(BiometricResponseCode.NotImplemented, response);
        }
        #endregion

        private int GetClientID()
        {
            return 101;
            //return _commonService.GetClientByKey(_httpContextAccessor.HttpContext.Request.Headers["subscriptionkey"].Single()).ClientId;
        }

    }
}
