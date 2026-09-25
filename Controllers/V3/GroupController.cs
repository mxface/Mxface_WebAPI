using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MxfaceWebAPI.Filters;
using MxfaceWebAPI.Models.Response.Group;
using MxfaceWebAPI.Services;

namespace MxfaceWebAPI.Controllers.V3
{
    // Subscription-key auth (via [APIAuthorizationFilter] on each action) gates these endpoints,
    // not the global JWT policy — AllowAnonymous opts out of that so the filter is what runs.
    // Shapes here follow the published Group API v3 developer guide exactly — see
    // GroupErrorResponse/GroupListResponse for the two shapes that differ from the rest of the app.
    [AllowAnonymous]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "Identity V3")]
    [ApiVersion("3.0")]
    public class GroupController : ControllerBase
    {
        private readonly IGroupService _groupService;

        public GroupController(IGroupService groupService)
        {
            _groupService = groupService;
        }

        // Resolved by APIAuthorizationFilterAttribute and stashed in HttpContext.Items — same
        // pattern as BiometricControllerBase.ResolvedClientId, kept local here since this
        // controller doesn't inherit that base.
        private long? ResolvedClientId =>
            HttpContext.Items.TryGetValue("ClientId", out var value) && value is long clientId ? clientId : null;

        // Also resolved by APIAuthorizationFilterAttribute and stashed alongside ClientId — used
        // to build the ABIS admin/clients URL in GroupService's calls to IAbisAdminApiClient.
        private string? ResolvedClientCode =>
            HttpContext.Items.TryGetValue("ClientCode", out var value) && value is string clientCode ? clientCode : null;
        private long? ResolvedUserId =>
            HttpContext.Items.TryGetValue("UserId", out var value) && value is long userId ? userId : null;

        [HttpGet("{groupId}", Name = "getByGroupId")]
        [APIAuthorizationFilter]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<IActionResult> Get([FromRoute] int groupId)
        {
            var clientId = ResolvedClientId!.Value;
            var group = await _groupService.GetGroupAsync(clientId, groupId);
            return group is null
                ? NotFound(GroupErrorResponse.Create(StatusCodes.Status404NotFound, "Could not find a Group with the specified ID"))
                : Ok(group);
        }

        [HttpGet(Name = "getByName")]
        [APIAuthorizationFilter]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<IActionResult> Get(string? groupName)
        {
            var clientId = ResolvedClientId!.Value;

            // Omitting groupName lists every group on the account; a name given is an exact,
            // case-sensitive lookup — either way the response is a wrapped array, never bare.
            if (string.IsNullOrWhiteSpace(groupName))
            {
                var all = await _groupService.ListGroupsAsync(clientId);
                return Ok(new GroupListResponse { Groups = all });
            }

            var match = await _groupService.GetGroupByNameAsync(clientId, groupName);
            var groups = match is null ? new List<Models.Response.Group.GroupResponse>() : new List<Models.Response.Group.GroupResponse> { match };
            return Ok(new GroupListResponse { Groups = groups });
        }

        [HttpPost(Name = "createGroup")]
        [APIAuthorizationFilter]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<IActionResult> Post([FromBody] Models.Request.Group.CreateGroupRequest? model)
        {
            if (model is null)
            {
                return BadRequest(GroupErrorResponse.Create(StatusCodes.Status400BadRequest, "Group information is not valid"));
            }

            var clientId = ResolvedClientId!.Value;
            var clientCode = ResolvedClientCode ?? string.Empty;
            var userId = ResolvedUserId!.Value;
            var result = await _groupService.CreateGroupAsync(clientId, model, clientCode, userId);
            return result.Success
                ? Ok(result.Group)
                : StatusCode(result.StatusCode, GroupErrorResponse.Create(result.StatusCode, result.ErrorMessage ?? "Failed to create group."));
        }

        [Route("{groupId}", Name = "UpdateGroup")]
        [HttpPut]
        [APIAuthorizationFilter]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<IActionResult> Put([FromRoute] int groupId, [FromBody] Models.Request.Group.CreateGroupRequest? model)
        {
            if (model is null)
            {
                return BadRequest(GroupErrorResponse.Create(StatusCodes.Status400BadRequest, "Group information is not valid"));
            }

            var clientId = ResolvedClientId!.Value;
            var clientCode = ResolvedClientCode ?? string.Empty;
            var result = await _groupService.UpdateGroupAsync(clientId, groupId, model, clientCode);
            return result.Success
                ? Ok(result.Group)
                : StatusCode(result.StatusCode, GroupErrorResponse.Create(result.StatusCode, result.ErrorMessage ?? "Failed to update group."));
        }

        [APIAuthorizationFilter]
        [HttpDelete]
        [Route("{groupId}", Name = "DeleteGroup")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<IActionResult> Delete([FromRoute] int groupId)
        {
            var clientId = ResolvedClientId!.Value;
            var clientCode = ResolvedClientCode ?? string.Empty;
            var result = await _groupService.DeleteGroupAsync(clientId, groupId, clientCode);
            return result.Success
                ? Ok(result.Group)
                : StatusCode(result.StatusCode, GroupErrorResponse.Create(result.StatusCode, result.ErrorMessage ?? "Failed to delete group."));
        }
    }
}
