using MxfaceWebAPI.Models.Request.Group;
using MxfaceWebAPI.Models.Response.Group;

namespace MxfaceWebAPI.Services
{
    // Groups are stored locally (faceclient_db.groups) as the source of truth for reads — no ABIS
    // call needed for Get/List, since a local row is only ever committed after ABIS confirms a
    // write succeeded. Writes go through IAbisAdminApiClient.UpdateClientGroupsAsync first; the
    // local row is only written/changed after that call succeeds.
    public interface IGroupService
    {
        Task<List<GroupResponse>> ListGroupsAsync(long clientId);
        Task<GroupResponse?> GetGroupAsync(long clientId, int groupId);
        Task<GroupResponse?> GetGroupByNameAsync(long clientId, string groupName);
        Task<GroupOperationResult> CreateGroupAsync(long clientId, CreateGroupRequest request,string clientCode);
        Task<GroupOperationResult> UpdateGroupAsync(long clientId, int groupId, CreateGroupRequest request, string clientCode);
        Task<GroupOperationResult> DeleteGroupAsync(long clientId, int groupId, string clientCode);
    }
}
