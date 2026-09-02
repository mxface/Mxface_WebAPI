using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MxfaceWebAPI.Filters;

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
        #region For Refrence Call
        public GroupController() { }
        #endregion

        // Stubs only — build-blocking references to legacy-only symbols (_logmanager,
        // _emailService, _groupService, ApiResponse, Entity.DBEntity.Group,
        // InternalAPI.Response.ParavisionAPI.GroupResponse, ReturnResponse) removed. Per the
        // ABIS Admin API guide (Step 5 of 7), group create/update/delete all go through the
        // client's PUT, not a dedicated group endpoint like this one implies — the real
        // implementation for these 5 actions is a separate, later task.

        [HttpGet("{groupId}", Name = "getByGroupId")]
        [APIAuthorizationFilter]
        [MapToApiVersion("3.0")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public Task<IActionResult> Get([FromRoute] int groupId)
        {
            return Task.FromResult<IActionResult>(StatusCode(501));
        }

        [HttpGet(Name = "getByName")]
        [APIAuthorizationFilter]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public Task<IActionResult> Get(string groupName)
        {
            return Task.FromResult<IActionResult>(StatusCode(501));
        }

        [HttpPost(Name = "createGroup")]
        [APIAuthorizationFilter]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public Task<IActionResult> Post([FromBody] Models.Request.Group.CreateGroupRequest model)
        {
            return Task.FromResult<IActionResult>(StatusCode(501));
        }

        [Route("{groupId}", Name = "UpdateGroup")]
        [HttpPut]
        [APIAuthorizationFilter]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public Task<IActionResult> Put([FromRoute] int groupId, [FromBody] Models.Request.Group.CreateGroupRequest model)
        {
            return Task.FromResult<IActionResult>(StatusCode(501));
        }

        [APIAuthorizationFilter]
        [HttpDelete]
        [Route("{groupId}", Name = "DeleteGroup")]
        [ApiExplorerSettings(GroupName = "Identity V3")]
        public Task<IActionResult> Delete([FromRoute] int groupId)
        {
            return Task.FromResult<IActionResult>(StatusCode(501));
        }
    }
}
