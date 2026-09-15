using MxfaceWebAPI.Models;

namespace MxfaceWebAPI.Data
{
    // Database-access layer for faceclient_db.groups. Every method is scoped by clientId so one
    // client's groups are never visible/editable by another. Not wired into any controller yet —
    // GroupController's REST endpoints depend on gRPC/Admin-API work that isn't implemented yet.
    public interface IGroupDataAccess
    {
        Task<Group?> GetGroupByIdAsync(long groupId, long clientId);
        Task<List<Group>> GetAllGroupsAsync(long clientId);
        Task<List<Group>> SearchGroupByNameAsync(string groupName, long clientId);
        Task<Group> AddGroupAsync(Group group);
        Task<bool> UpdateGroupAsync(Group group);
        Task<bool> DeleteGroupAsync(long groupId, long clientId);
    }
}
