using MxfaceWebAPI.Models;

namespace MxfaceWebAPI.Data
{
    public class GroupDataAccess : IGroupDataAccess
    {
        // "id" is aliased to "groupid" so ExecuteSelectAsync's reflective mapper (which matches
        // columns to properties by name) lines up with Group.GroupId — every other column
        // already matches its property name case-insensitively.
        private const string SelectColumns =
            "id as groupid, clientid, groupcode, groupname, description, isdefault, abisgroupid, createdby, createdat, modifyby, modifyat";

        private readonly IPostgresHelper _postgresHelper;
        private readonly ILogger<GroupDataAccess> _logger;

        public GroupDataAccess(IPostgresHelper postgresHelper, ILogger<GroupDataAccess> logger)
        {
            _postgresHelper = postgresHelper;
            _logger = logger;
        }

        public async Task<Group?> GetGroupByIdAsync(long groupId, long clientId)
        {
            var sql = $"select {SelectColumns} from faceclient_db.groups where id = $1 and clientid = $2";
            var results = await _postgresHelper.ExecuteSelectAsync<Group>(sql, groupId, clientId);
            return results.FirstOrDefault();
        }

        public Task<List<Group>> GetAllGroupsAsync(long clientId)
        {
            var sql = $"select {SelectColumns} from faceclient_db.groups where clientid = $1 order by groupname";
            return _postgresHelper.ExecuteSelectAsync<Group>(sql, clientId);
        }

        public Task<List<Group>> SearchGroupByNameAsync(string groupName, long clientId)
        {
            var sql = $"select {SelectColumns} from faceclient_db.groups where clientid = $1 and groupname ilike '%' || $2 || '%' order by groupname";
            return _postgresHelper.ExecuteSelectAsync<Group>(sql, clientId, groupName);
        }

        public async Task<Group> AddGroupAsync(Group group)
        {
            const string sql = """
                insert into faceclient_db.groups (clientid, groupcode, groupname, description, isdefault, abisgroupid, createdby, createdat)
                values ($1, $2, $3, $4, $5, $6, $7, now())
                returning id
                """;

            var insertedId = await _postgresHelper.ExecuteScalarAsync(
                sql, group.ClientId, group.GroupCode, group.GroupName, group.Description, group.IsDefault, group.AbisGroupId, group.CreatedBy);

            group.GroupId = Convert.ToInt64(insertedId);
            return group;
        }

        public async Task<bool> UpdateGroupAsync(Group group)
        {
            const string sql = """
                update faceclient_db.groups
                set groupcode = $3, groupname = $4, description = $5, isdefault = $6, abisgroupid = $7, modifyby = $8, modifyat = now()
                where id = $1 and clientid = $2
                """;

            var affected = await _postgresHelper.ExecuteNonQueryAsync(
                sql, group.GroupId, group.ClientId, group.GroupCode, group.GroupName, group.Description, group.IsDefault, group.AbisGroupId, group.ModifyBy);

            return affected > 0;
        }

        public async Task<bool> DeleteGroupAsync(long groupId, long clientId)
        {
            const string sql = "delete from faceclient_db.groups where id = $1 and clientid = $2";
            var affected = await _postgresHelper.ExecuteNonQueryAsync(sql, groupId, clientId);
            return affected > 0;
        }
    }
}
