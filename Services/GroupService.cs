using Microsoft.AspNetCore.Http;
using MxfaceWebAPI.Common;
using MxfaceWebAPI.Data;
using MxfaceWebAPI.Models;
using MxfaceWebAPI.Models.AdminApi;
using MxfaceWebAPI.Models.Request.Group;
using MxfaceWebAPI.Models.Response.Group;

namespace MxfaceWebAPI.Services
{
    public class GroupService : IGroupService
    {
        private readonly IGroupDataAccess _groupDataAccess;
        private readonly IPostgresHelper _postgresHelper;
        private readonly IAbisAdminApiClient _abisAdminApiClient;
        private readonly ILogger<GroupService> _logger;

        public GroupService(
            IGroupDataAccess groupDataAccess,
            IPostgresHelper postgresHelper,
            IAbisAdminApiClient abisAdminApiClient,
            ILogger<GroupService> logger)
        {
            _groupDataAccess = groupDataAccess;
            _postgresHelper = postgresHelper;
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
            if (string.IsNullOrWhiteSpace(request.GroupName))
            {
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, "GroupName is required.");
            }

            //var abisClientId = await GetAbisClientIdAsync(clientId).ConfigureAwait(false);
            //if (abisClientId is null)
            //{
            //    return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, "This client is not linked for ABIS group management.");
            //}

            var delta = new GroupsDelta
            {
                Add = new List<AdminClientGroup>
                {
                    new() { GroupName = request.GroupName, IsDefault = request.IsDefault ?? false, Description = request.Description }
                }
            };            
            var _cid=clientId;
            var response = await _abisAdminApiClient.UpdateClientGroupsAsync(Convert.ToInt32(_cid), delta,clientCode).ConfigureAwait(false);
            //var response = await _abisAdminApiClient.UpdateClientGroupsAsync(abisClientId.Value, delta).ConfigureAwait(false);
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

        public async Task<GroupOperationResult> UpdateGroupAsync(long clientId, int groupId, CreateGroupRequest request,string Code)
        {
            var existing = await _groupDataAccess.GetGroupByIdAsync(groupId, clientId).ConfigureAwait(false);
            if (existing is null)
            {
                return GroupOperationResult.Fail(StatusCodes.Status404NotFound, "Group not found.");
            }

            if (!string.IsNullOrWhiteSpace(request.GroupName) &&
                !string.Equals(request.GroupName, existing.GroupName, StringComparison.Ordinal))
            {
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, "Group name cannot be changed once created.");
            }

            if (existing.AbisGroupId is null)
            {
                return GroupOperationResult.Fail(BiometricResponseCode.ServiceUnavailable, "This group was never successfully synced to ABIS.");
            }

            var abisClientId = await GetAbisClientIdAsync(clientId).ConfigureAwait(false);
            if (abisClientId is null)
            {
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, "This client is not linked for ABIS group management.");
            }

            var delta = new GroupsDelta
            {
                Update = new List<AdminClientGroupUpdate>
                {
                    new() { Id = existing.AbisGroupId.Value, IsDefault = request.IsDefault, Description = request.Description }
                }
            };

            var response = await _abisAdminApiClient.UpdateClientGroupsAsync(abisClientId.Value, delta,Code).ConfigureAwait(false);
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

        public async Task<GroupOperationResult> DeleteGroupAsync(long clientId, int groupId, string Code)
        {
            var existing = await _groupDataAccess.GetGroupByIdAsync(groupId, clientId).ConfigureAwait(false);
            if (existing is null)
            {
                return GroupOperationResult.Fail(StatusCodes.Status404NotFound, "Group not found.");
            }

            if (existing.AbisGroupId is null)
            {
                return GroupOperationResult.Fail(BiometricResponseCode.ServiceUnavailable, "This group was never successfully synced to ABIS.");
            }

            var abisClientId = await GetAbisClientIdAsync(clientId).ConfigureAwait(false);
            if (abisClientId is null)
            {
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, "This client is not linked for ABIS group management.");
            }

            var delta = new GroupsDelta { Remove = new List<int> { existing.AbisGroupId.Value } };

            var response = await _abisAdminApiClient.UpdateClientGroupsAsync(abisClientId.Value, delta, Code).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                // Surfaces ABIS's own message as-is — e.g. "still assigned to existing records" —
                // rather than swallowing it, since that's a real, actionable rejection, not a 500.
                _logger.LogWarning("DeleteGroup: ABIS rejected remove for group {GroupId} (client {ClientId}): {Error}", groupId, clientId, response.ErrorMessage);
                return GroupOperationResult.Fail(BiometricResponseCode.BadRequest, response.ErrorMessage ?? "Failed to delete group.");
            }

            await _groupDataAccess.DeleteGroupAsync(groupId, clientId).ConfigureAwait(false);
            return GroupOperationResult.Ok();
        }

        private async Task<int?> GetAbisClientIdAsync(long clientId)
        {
            var value = await _postgresHelper.ExecuteScalarAsync(
                "select abisclientid from faceclient_db.clients where id = $1", clientId).ConfigureAwait(false);
            return value is int abisClientId ? abisClientId : null;
        }

        // No format was specified for this — a short random code, since callers never supply one
        // and nothing else in the codebase reads/parses it back.
        private static string GenerateGroupCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        private static GroupResponse ToResponse(Group group) => new()
        {
            GroupId = (int)group.GroupId,
            GroupName = group.GroupName,
            Description = group.Description,
            IsDefault = group.IsDefault,
            CreatedDate = group.CreatedAt,
            UpdatedDate = group.ModifyAt ?? group.CreatedAt
        };
    }
}
