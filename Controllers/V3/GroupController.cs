using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MxfaceWebAPI.Filters;
using MxfaceWebAPI.Models;
using MxfaceWebAPI.Services;

namespace MxfaceWebAPI.Controllers.V3
{
    // Subscription-key auth (via [APIAuthorizationFilter] on each action) gates these endpoints,
    // not the global JWT policy — AllowAnonymous opts out of that so the filter is what runs.
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


        // Also resolved by APIAuthorizationFilterAttribute and stashed alongside ClientId — not
        // used by any action yet (they still scope via ResolvedClientId/IGroupService as before);
        // kept available for whatever needs it later.
        private string? ResolvedClientCode =>
            HttpContext.Items.TryGetValue("ClientCode", out var value) && value is string clientCode ? clientCode : null;


        [HttpGet("{groupId}", Name = "getByGroupId")]
        [APIAuthorizationFilter]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<IActionResult> Get([FromRoute] int groupId)
        {
            var clientId = ResolvedClientId!.Value;
            var group = await _groupService.GetGroupAsync(clientId, groupId);
            return group is null
                ? NotFound(new ApiErrorResponse { Code = StatusCodes.Status404NotFound, Error = "Group not found." })
                : Ok(group);
        }

        [HttpGet(Name = "getByName")]
        [APIAuthorizationFilter]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<IActionResult> Get(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return BadRequest(new ApiErrorResponse { Code = StatusCodes.Status400BadRequest, Error = "groupName is required." });
            }

            var clientId = ResolvedClientId!.Value;
            var group = await _groupService.GetGroupByNameAsync(clientId, groupName);
            return group is null
                ? NotFound(new ApiErrorResponse { Code = StatusCodes.Status404NotFound, Error = "Group not found." })
                : Ok(group);
        }

        [HttpPost(Name = "createGroup")]
        [APIAuthorizationFilter]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<IActionResult> Post([FromBody] Models.Request.Group.CreateGroupRequest model)
        {
            if (model is null || string.IsNullOrWhiteSpace(model.GroupName))
            {
                return BadRequest(new ApiErrorResponse { Code = StatusCodes.Status400BadRequest, Error = "GroupName is required." });
            }

            var clientId = ResolvedClientId!.Value;
            var clientCode = ResolvedClientCode ?? string.Empty;
            var result = await _groupService.CreateGroupAsync(clientId, model, clientCode);
            return result.Success
                ? Ok(result.Group)
                : StatusCode(result.StatusCode, new ApiErrorResponse { Code = result.StatusCode, Error = result.ErrorMessage ?? "Failed to create group." });
        }

        [Route("{groupId}", Name = "UpdateGroup")]
        [HttpPut]
        [APIAuthorizationFilter]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public async Task<IActionResult> Put([FromRoute] int groupId, [FromBody] Models.Request.Group.CreateGroupRequest model)
        {
            if (model is null)
            {
                return BadRequest(new ApiErrorResponse { Code = StatusCodes.Status400BadRequest, Error = "Request body is required." });
            }

            var clientId = ResolvedClientId!.Value;
            var clientCode = ResolvedClientCode ?? string.Empty;
            var result = await _groupService.UpdateGroupAsync(clientId, groupId, model, clientCode);
            return result.Success
                ? Ok(result.Group)
                : StatusCode(result.StatusCode, new ApiErrorResponse { Code = result.StatusCode, Error = result.ErrorMessage ?? "Failed to update group." });
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
                ? Ok()
                : StatusCode(result.StatusCode, new ApiErrorResponse { Code = result.StatusCode, Error = result.ErrorMessage ?? "Failed to delete group." });
        }
    }
}
