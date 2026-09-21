using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using MxfaceWebAPI.Common;
using MxfaceWebAPI.Data;
using MxfaceWebAPI.Models.AdminApi;
using MxfaceWebAPI.Models.Request.Group;
using MxfaceWebAPI.Models.Response.Group;
using Group = MxfaceWebAPI.Models.Group;

namespace MxfaceWebAPI.Services
{
    public class GroupService : IGroupService
    {
        // Matches the documented Group API v3 contract: letters, digits, spaces, and \ _ [ ] ( ) -
        private const int MaxGroupNameLength = 30;
        private static readonly Regex ValidGroupNamePattern = new(@"^[A-Za-z0-9 \\_\[\]\(\)\-]+$", RegexOptions.Compiled);

        private readonly IGroupDataAccess _groupDataAccess;
        private readonly IAbisAdminApiClient _abisAdminApiClient;
        private readonly ILogger<GroupService> _logger;

        public GroupService(
            IGroupDataAccess groupDataAccess,
            IAbisAdminApiClient abisAdminApiClient,
            ILogger<GroupService> logger)
        {
            _groupDataAccess = groupDataAccess;
            _abisAdminApiClient = abisAdminApiClient;
            _logger = logger;
        }

        public async Task<List<GroupResponse>> ListGroupsAsync(long clientId)
        {
            var groups = await _groupDataAccess.GetAllGroupsAsync(clientId).ConfigureAwait(false);
            return groups.Select(ToResponse).ToList();
        }

        public async Task<GroupResponse?> GetGroupAsync(long clientId, int groupId)
        {
            var group = await _groupDataAccess.GetGroupByIdAsync(groupId, clientId).ConfigureAwait(false);
            return group is null ? null : ToResponse(group);
        }

        public async Task<GroupResponse?> GetGroupByNameAsync(long clientId, string groupName)
        {
            var groups = await _groupDataAccess.SearchGroupByNameAsync(groupName, clientId).ConfigureAwait(false);
            var exact = groups.FirstOrDefault(g => string.Equals(g.GroupName, groupName, StringComparison.OrdinalIgnoreCase));
            return exact is null ? null : ToResponse(exact);
        }

        public async Task<GroupOperationResult> CreateGroupAsync(long clientId, CreateGroupRequest request, string clientCode)
        {
            var validationError = ValidateGroupName(request.GroupName, requireGroupId: false, groupId: null);
            if (validationError is not null)
            {
                return validationError;
            }

            var duplicate = await GetGroupByNameAsync(clientId, request.GroupName).ConfigureAwait(false);
            if (duplicate is not null)
            {
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, $"Group name {request.GroupName} already exists");
            }

            var delta = new GroupsDelta
            {
                Add = new List<AdminClientGroup>
                {
                    new() { GroupName = request.GroupName, IsDefault = request.IsDefault ?? false, Description = request.Description }
                }
            };

            var cid = Convert.ToInt32(clientId);
            var response = await _abisAdminApiClient.UpdateClientGroupsAsync(cid, delta, clientCode).ConfigureAwait(false);
            if (!response.IsSuccess || response.Data is null)
            {
                _logger.LogError("CreateGroup: ABIS rejected add for client {ClientId}: {Error}", clientId, response.ErrorMessage);
                return GroupOperationResult.Fail(BiometricResponseCode.ServiceUnavailable, response.ErrorMessage ?? "Failed to create group.");
            }

            var created = response.Data.Groups
                .FirstOrDefault(g => string.Equals(g.GroupName, request.GroupName, StringComparison.OrdinalIgnoreCase));
            if (created?.Id is null)
            {
                _logger.LogError("CreateGroup: ABIS accepted the add but no matching group was found in the response for client {ClientId}", clientId);
                return GroupOperationResult.Fail(BiometricResponseCode.ServiceUnavailable, "Group was created but its id could not be resolved from the response.");
            }

            // No per-user identity exists in this project's subscription-key-only auth model —
            // ClientId stands in for CreatedBy/ModifyBy, same actor-of-record for every group row.
            var group = new Group
            {
                ClientId = clientId,
                GroupCode = GenerateGroupCode(),
                GroupName = request.GroupName,
                Description = request.Description,
                IsDefault = request.IsDefault ?? false,
                AbisGroupId = created.Id.Value,
                CreatedBy = clientId
            };

            var inserted = await _groupDataAccess.AddGroupAsync(group).ConfigureAwait(false);
            return GroupOperationResult.Ok(ToResponse(inserted));
        }

        public async Task<GroupOperationResult> UpdateGroupAsync(long clientId, int groupId, CreateGroupRequest request, string clientCode)
        {
            var validationError = ValidateGroupName(request.GroupName, requireGroupId: true, groupId: groupId);
            if (validationError is not null)
            {
                return validationError;
            }

            var existing = await _groupDataAccess.GetGroupByIdAsync(groupId, clientId).ConfigureAwait(false);
            if (existing is null)
            {
                return GroupOperationResult.Fail(StatusCodes.Status404NotFound, "Could not find a Group with the specified ID");
            }

            // Confirmed with the ABIS API team: groupName can never be changed once a group is
            // created — not even via a remove+add — only description/isDefault are updatable.
            if (!string.Equals(request.GroupName, existing.GroupName, StringComparison.Ordinal))
            {
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, "Group name cannot be changed once created.");
            }

            if (existing.AbisGroupId is null)
            {
                return GroupOperationResult.Fail(BiometricResponseCode.ServiceUnavailable, "This group was never successfully synced to ABIS.");
            }

            var delta = new GroupsDelta
            {
                Update = new List<AdminClientGroupUpdate>
                {
                    new() { Id = existing.AbisGroupId.Value, IsDefault = request.IsDefault, Description = request.Description }
                }
            };

            var cid = Convert.ToInt32(clientId);
            var response = await _abisAdminApiClient.UpdateClientGroupsAsync(cid, delta, clientCode).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                _logger.LogError("UpdateGroup: ABIS rejected update for group {GroupId} (client {ClientId}): {Error}", groupId, clientId, response.ErrorMessage);
                return GroupOperationResult.Fail(BiometricResponseCode.ServiceUnavailable, response.ErrorMessage ?? "Failed to update group.");
            }

            // Merge-patch on the ABIS side (omitted fields keep their value) — mirror that here:
            // only overwrite locally what the caller actually sent.
            existing.Description = request.Description ?? existing.Description;
            existing.IsDefault = request.IsDefault ?? existing.IsDefault;
            existing.ModifyBy = clientId;

            var updated = await _groupDataAccess.UpdateGroupAsync(existing).ConfigureAwait(false);
            if (!updated)
            {
                return GroupOperationResult.Fail(BiometricResponseCode.ServiceUnavailable, "ABIS accepted the update but the local copy could not be updated.");
            }

            return GroupOperationResult.Ok(ToResponse(existing));
        }

        public async Task<GroupOperationResult> DeleteGroupAsync(long clientId, int groupId, string clientCode)
        {
            if (groupId <= 0)
            {
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, "Group id required");
            }

            var existing = await _groupDataAccess.GetGroupByIdAsync(groupId, clientId).ConfigureAwait(false);
            if (existing is null)
            {
                return GroupOperationResult.Fail(StatusCodes.Status404NotFound, "Could not find a Group with the specified ID");
            }

            if (existing.AbisGroupId is null)
            {
                return GroupOperationResult.Fail(BiometricResponseCode.ServiceUnavailable, "This group was never successfully synced to ABIS.");
            }

            var delta = new GroupsDelta { Remove = new List<int> { existing.AbisGroupId.Value } };

            var cid = Convert.ToInt32(clientId);
            var response = await _abisAdminApiClient.UpdateClientGroupsAsync(cid, delta, clientCode).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                // Surfaces ABIS's own message as-is — e.g. "still assigned to existing records" —
                // rather than swallowing it, since that's a real, actionable rejection, not a 500.
                _logger.LogWarning("DeleteGroup: ABIS rejected remove for group {GroupId} (client {ClientId}): {Error}", groupId, clientId, response.ErrorMessage);
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, response.ErrorMessage ?? "Group cannot delete because it is being used by face identity");
            }

            // Capture the response before the row is gone — the documented contract returns the
            // deleted group's data, not an empty body.
            var deletedResponse = ToResponse(existing);

            await _groupDataAccess.DeleteGroupAsync(groupId, clientId).ConfigureAwait(false);
            return GroupOperationResult.Ok(deletedResponse);
        }

        // Shared validation matching the documented Group API v3 messages and order exactly:
        // groupName blank, then groupId missing/0 (update/delete only), then length, then charset.
        private static GroupOperationResult? ValidateGroupName(string? groupName, bool requireGroupId, int? groupId)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, "Group name is required");
            }

            if (requireGroupId && (groupId is null || groupId <= 0))
            {
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, "Group id required");
            }

            if (groupName.Length > MaxGroupNameLength)
            {
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, "Group name maximum 30 character as long");
            }

            if (!ValidGroupNamePattern.IsMatch(groupName))
            {
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, "only \\, _,[,],(,),- special characters and space is allowed");
            }

            return null;
        }

        // No format was specified for this — a short random code, since callers never supply one
        // and nothing else in the codebase reads/parses it back.
        private static string GenerateGroupCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        private static GroupResponse ToResponse(Group group) => new()
        {
            GroupId = (int)group.GroupId,
            GroupName = group.GroupName,
            CreatedDate = group.CreatedAt,
            UpdatedDate = group.ModifyAt ?? group.CreatedAt
        };
    }
}
