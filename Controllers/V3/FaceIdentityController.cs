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
        private const int DeleteMode = 0;

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
        private readonly IFaceIdentityMapDataAccess _faceIdentityMapDataAccess;

        // Resolved by APIAuthorizationFilterAttribute and stashed in HttpContext.Items — same
        // pattern as BiometricControllerBase.ResolvedClientId, kept local here since this
        // controller doesn't inherit that base.
        private long? ResolvedClientId =>
            HttpContext.Items.TryGetValue("ClientId", out var value) && value is long clientId ? clientId : null;

        // Also resolved by APIAuthorizationFilterAttribute and stashed alongside ClientId — used
        // to build the ABIS admin/clients URL in GroupService's calls to IAbisAdminApiClient.
        private string? ResolvedClientCode =>
            HttpContext.Items.TryGetValue("ClientCode", out var value) && value is string clientCode ? clientCode : null;

        // client_users.id of the caller — stored as createdby/updatedby on metadata rows.
        private long? ResolvedUserId =>
            HttpContext.Items.TryGetValue("UserId", out var value) && value is long userId ? userId : null;

        #region For Refrence Call
        public FaceIdentityController(ClientApiService.ClientApiServiceClient clientApiClient, IClientApiEnvelopeFactory envelopeFactory,
            IConfiguration configuration, IPostgresHelper postgresHelper, ILogger<FaceIdentityController> logger,
            IEmailService emailService, IGroupService groupService, IFaceIdentityMapDataAccess faceIdentityMapDataAccess) : base(envelopeFactory, configuration, postgresHelper, logger)

        {
            _clientApiClient = clientApiClient;
            _emailService = emailService;
            _config = configuration;
            _groupService = groupService;
            _faceIdentityMapDataAccess = faceIdentityMapDataAccess;
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

                // ABIS enrolled every group — only now record the identity locally. face_identity_map
                // holds one group per identity (unique on clientid+externalid), so the first group is stored.
                var faceIdentityId = await _faceIdentityMapDataAccess.InsertAsync(
                    clientId, model.externalId, groups[0].GroupId, ResolvedUserId);

                enrollResult.FaceIdentityId = (int)faceIdentityId;
                enrollResult.externalId = model.externalId;
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


        #region Get Identity
        /// <summary>
        /// Get identity by external id — read from faceclient_db.face_identity_map only (no ABIS
        /// call: ABIS has no "get applicant" API). Faces are not returned since face data lives
        /// in ABIS, not on our side.
        /// </summary>
        /// <param name="externalId"></param>
        /// <returns></returns>
        [HttpGet]
        [APIAuthorizationFilter]
        [MapToApiVersion("3.0")]
        [Route("{externalId}/facIdentities", Name = "GetFaceByExternalId")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<FaceIdentityInfo>> Get([FromRoute] string externalId)
        {
            if (string.IsNullOrWhiteSpace(externalId))
            {
                return await ReturnResponse(new FaceIdentityInfo
                {
                    ErrorCode = BiometricResponseCode.BadRequest,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest + " External id required"
                }).ConfigureAwait(true);
            }

            var identity = await _faceIdentityMapDataAccess.GetByExternalIdAsync(externalId, ResolvedClientId!.Value);
            return await ReturnResponse(ToFaceIdentityInfoOrNotFound(identity)).ConfigureAwait(true);
        }

        /// <summary>
        /// Get identity by face identity id — read from faceclient_db.face_identity_map only
        /// (no ABIS call). Faces are not returned.
        /// </summary>
        [HttpGet("{faceIdentityId}", Name = "GetByIdentityId")]
        [APIAuthorizationFilter]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<FaceIdentityInfo>> Get([FromRoute] int faceIdentityId)
        {
            if (faceIdentityId <= 0)
            {
                return await ReturnResponse(new FaceIdentityInfo
                {
                    ErrorCode = BiometricResponseCode.BadRequest,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest
                }).ConfigureAwait(true);
            }

            var identity = await _faceIdentityMapDataAccess.GetByFaceIdentityIdAsync(faceIdentityId, ResolvedClientId!.Value);
            return await ReturnResponse(ToFaceIdentityInfoOrNotFound(identity)).ConfigureAwait(true);
        }

        private static FaceIdentityInfo ToFaceIdentityInfoOrNotFound(FaceIdentityMap? identity) =>
            identity == null
                ? new FaceIdentityInfo { ErrorCode = StatusCodes.Status404NotFound, ErrorMessage = "Identity not found." }
                : ToFaceIdentityInfo(identity);

        // Faces is deliberately left null — the global WhenWritingNull JSON option (Program.cs)
        // then drops it from the response entirely. Metadata comes from face_identity_metadata
        // (joined in the same query), {} when the identity has none.
        private static FaceIdentityInfo ToFaceIdentityInfo(FaceIdentityMap identity)
        {
            var groups = new List<Models.Response.Group.GroupResponse>();
            if (identity.GroupId.HasValue)
            {
                var groupCreatedAt = identity.GroupCreatedAt ?? identity.CreatedAt;
                groups.Add(new Models.Response.Group.GroupResponse
                {
                    GroupId = (int)identity.GroupId.Value,
                    GroupName = identity.GroupName ?? string.Empty,
                    CreatedDate = groupCreatedAt,
                    UpdatedDate = identity.GroupModifyAt ?? groupCreatedAt
                });
            }

            return new FaceIdentityInfo
            {
                FaceIdentityId = (int)identity.FaceIdentityId,
                Groups = groups,
                CreatedDate = identity.CreatedAt,
                externalId = identity.ExternalId,
                UpdatedDate = identity.ModifyAt ?? identity.CreatedAt,
                Metadata = ParseMetadata(identity.Metadata)
            };
        }
        #endregion

        /// <summary>
        /// List identities of the client, optionally filtered by group, paged by offset/limit —
        /// read from faceclient_db.face_identity_map only (no ABIS call).
        /// </summary>
        [APIAuthorizationFilter]
        [HttpGet(Name = "GetIdentityByGroup")]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<Models.Response.FaceIdentity.FaceIdentityResponse>> Get(int? groupId, int offset = 0, int limit = 10)
        {
            // Same limit/offset rules and plain-text 400s as the legacy API.
            if (limit > 100)
            {
                return BadRequest("Limit cannot be greater than 100");
            }
            else if (limit <= 0)
            {
                return BadRequest("Limit should be greater than 0");
            }
            if (offset < 0)
            {
                offset = 0;
            }

            try
            {
                var clientId = ResolvedClientId!.Value;
                var identities = await _faceIdentityMapDataAccess.GetListAsync(clientId, groupId, offset, limit);

                var response = new Models.Response.FaceIdentity.FaceIdentityResponse
                {
                    FaceIdentities = identities.Select(ToFaceIdentityInfo).ToList(),
                    TotalFaceIdentities = identities.Count > 0
                        ? await _faceIdentityMapDataAccess.GetCountAsync(clientId, groupId)
                        : 0
                };
                return await ReturnResponse(response).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", "GetIdentityByGroup");
                await _emailService.ExceptionMailSend("Identity_GetByGroupId", ex).ConfigureAwait(true);
                return await ReturnResponse(new Models.Response.FaceIdentity.FaceIdentityResponse
                {
                    ErrorCode = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.GeneralErrorMessage
                }).ConfigureAwait(true);
            }
        }

        // Delete responses carry only code/message — Metadata defaults to {} on FaceIdentityInfo,
        // so it's nulled here and the global WhenWritingNull option drops it from the JSON.
        private Task<ActionResult<FaceIdentityInfo>> DeleteResponse(FaceIdentityInfo response)
        {
            response.Metadata = null!;
            return ReturnResponse(response);
        }

        [HttpDelete("{faceIdentityId}", Name = "DeleteIdentity")]
        [APIAuthorizationFilter]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<FaceIdentityInfo>> Delete([FromRoute] int faceIdentityId)
        {
            if (faceIdentityId <= 0)
            {
                return await DeleteResponse(new FaceIdentityInfo
                {
                    ErrorCode = BiometricResponseCode.BadRequest,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest + "FaceIdentity Id is required"
                }).ConfigureAwait(true);
            }

            try
            {
                var clientId = ResolvedClientId!.Value;

                // faceIdentityId -> externalId (= ABIS referenceId) + groupName, both needed by ABIS Delete.
                var identity = await _faceIdentityMapDataAccess.GetByFaceIdentityIdAsync(faceIdentityId, clientId);
                if (identity == null)
                {
                    return await DeleteResponse(new FaceIdentityInfo
                    {
                        ErrorCode = StatusCodes.Status404NotFound,
                        ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.IdentityNotExists
                    }).ConfigureAwait(true);
                }

                // groupid has no foreign key, so the group may be missing or deleted — ABIS can't
                // delete without a groupName, so keep the row rather than orphan the ABIS record.
                if (string.IsNullOrWhiteSpace(identity.GroupName))
                {
                    return await DeleteResponse(new FaceIdentityInfo
                    {
                        ErrorCode = BiometricResponseCode.BadRequest,
                        ErrorMessage = "Group not found for this identity."
                    }).ConfigureAwait(true);
                }

                // Delete from ABIS first: if it fails the local row stays and the client can retry.
                var abisResult = await CallAsync<BiomatricBaseResponse>(
                    DeleteMode,
                    new DeleteMasterPayload
                    {
                        GroupName = identity.GroupName,
                        ReferenceIds = new List<string> { identity.ExternalId }
                    },
                    (r, o) => _clientApiClient.DeleteAsync(r, o).ResponseAsync,
                    nameof(Delete),
                    BiometricFeatureType.FaceDelete);

                // "Citizen not found" means ABIS no longer has this person, so the local row is
                // stale — remove it too instead of leaving an identity that can never be deleted.
                var abisNotFound = abisResult.Message == MasterErrorMapper.ExternalIdNotFoundMessage;
                if (abisResult.Code != BiometricResponseCode.Success && !abisNotFound)
                {
                    return await DeleteResponse(new FaceIdentityInfo
                    {
                        ErrorCode = abisResult.Code ?? BiometricResponseCode.ServiceUnavailable,
                        ErrorMessage = abisResult.ErrorMessage ?? abisResult.Message
                    }).ConfigureAwait(true);
                }

                if (abisNotFound)
                {
                    _logger.LogWarning("{Operation}: ABIS had no record for faceIdentityId {FaceIdentityId}; removing the local row only",
                        nameof(Delete), faceIdentityId);
                }

                await _faceIdentityMapDataAccess.DeleteAsync(faceIdentityId, clientId, ResolvedUserId);

                return await DeleteResponse(new FaceIdentityInfo
                {
                    Code = BiometricResponseCode.Success,
                    Message = "Identity deleted successfully."
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", nameof(Delete));
                await _emailService.ExceptionMailSend("Identity_DeleteIdentity", ex).ConfigureAwait(true);
                return await DeleteResponse(new FaceIdentityInfo
                {
                    ErrorCode = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.GeneralErrorMessage
                }).ConfigureAwait(true);
            }
        }


        [HttpPost]
        [APIAuthorizationFilter]
        [Route("{faceIdentityId}/addMetadata", Name = "AddMetadata")]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<ActionResult<MetadataInfo>> Post([FromRoute] int faceIdentityId, [FromBody] MetadataRequest request)
        {
            if (request?.Metadata == null || !request.Metadata.Any())
            {
                return await ReturnResponse(new MetadataInfo { ErrorCode = StatusCodes.Status400BadRequest, ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest }).ConfigureAwait(true);
            }
            if (faceIdentityId <= 0)
            {
                return await ReturnResponse(new MetadataInfo { ErrorCode = StatusCodes.Status400BadRequest, ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest }).ConfigureAwait(true);
            }

            try
            {
                var clientId = ResolvedClientId!.Value;

                // Metadata may only be added for an identity that exists for this client.
                var identity = await _faceIdentityMapDataAccess.GetByFaceIdentityIdAsync(faceIdentityId, clientId);
                if (identity == null)
                {
                    return await ReturnResponse(new MetadataInfo
                    {
                        ErrorCode = StatusCodes.Status404NotFound,
                        ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.IdentityNotExists
                    }).ConfigureAwait(true);
                }

                var added = await _faceIdentityMapDataAccess.AddMetadataAsync(
                    faceIdentityId, clientId, System.Text.Json.JsonSerializer.Serialize(request.Metadata), ResolvedUserId);

                if (added == null)
                {
                    // Same contract as legacy: already-present metadata is a 400 that echoes it back.
                    var existing = await _faceIdentityMapDataAccess.GetMetadataAsync(faceIdentityId, clientId);
                    return await ReturnResponse(new MetadataInfo
                    {
                        Metadata = ParseMetadata(existing),
                        FaceIdentityId = faceIdentityId,
                        Code = StatusCodes.Status400BadRequest,
                        ErrorCode = StatusCodes.Status400BadRequest,
                        ErrorMessage = "Metadata already exists for this identity."
                    }).ConfigureAwait(true);
                }

                return await ReturnResponse(new MetadataInfo
                {
                    Metadata = ParseMetadata(added),
                    FaceIdentityId = faceIdentityId,
                    Code = StatusCodes.Status200OK
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", "AddMetadata");
                await _emailService.ExceptionMailSend("Identity_AddMetadata", ex).ConfigureAwait(true);
                return await ReturnResponse(new MetadataInfo
                {
                    ErrorCode = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.GeneralErrorMessage
                }).ConfigureAwait(true);
            }
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

            if (request?.Metadata == null || !request.Metadata.Any())
            {
                return await ReturnResponse(new MetadataInfo { ErrorCode = StatusCodes.Status400BadRequest, ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.InValidRequest }).ConfigureAwait(true);
            }

            try
            {
                var clientId = ResolvedClientId!.Value;

                var identity = await _faceIdentityMapDataAccess.GetByFaceIdentityIdAsync(faceIdentityId, clientId);
                if (identity == null)
                {
                    return await ReturnResponse(new MetadataInfo
                    {
                        ErrorCode = StatusCodes.Status404NotFound,
                        ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.IdentityNotExists
                    }).ConfigureAwait(true);
                }

                var updated = await _faceIdentityMapDataAccess.UpdateMetadataAsync(
                    faceIdentityId, clientId, System.Text.Json.JsonSerializer.Serialize(request.Metadata), ResolvedUserId);

                if (updated == null)
                {
                    return await ReturnResponse(new MetadataInfo
                    {
                        Code = StatusCodes.Status400BadRequest,
                        ErrorCode = StatusCodes.Status400BadRequest,
                        ErrorMessage = "Not metadata found to update."
                    }).ConfigureAwait(true);
                }

                return await ReturnResponse(new MetadataInfo
                {
                    Metadata = ParseMetadata(updated),
                    FaceIdentityId = faceIdentityId,
                    Code = StatusCodes.Status200OK
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Operation} failed unexpectedly", "UpdateMetadata");
                await _emailService.ExceptionMailSend("Identity_UpdateMetadata", ex).ConfigureAwait(true);
                return await ReturnResponse(new MetadataInfo
                {
                    ErrorCode = BiometricResponseCode.ServiceUnavailable,
                    ErrorMessage = global::MxfaceWebAPI.CommonHelper.CommonHelper.GeneralErrorMessage
                }).ConfigureAwait(true);
            }
        }

        // Metadata is stored as a JSON object of string values (it's written from
        // Dictionary<string, string>), so it reads back into the same shape.
        private static Dictionary<string, string> ParseMetadata(string? metadataJson) =>
            string.IsNullOrWhiteSpace(metadataJson)
                ? []
                : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? [];


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
