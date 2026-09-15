namespace MxfaceWebAPI.Models
{
    // Plain row model for faceclient_db.groups — GroupId maps to the table's "id" column
    // (every query in GroupDataAccess aliases it as "id as groupid" so the reflective mapper's
    // name-matching lines up); every other property already matches its column name
    // case-insensitively.
    public class Group
    {
        public long GroupId { get; set; }
        public long ClientId { get; set; }
        public string GroupCode { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsDefault { get; set; }

        /// <summary>The group's numeric id on the ABIS Admin API side, once successfully synced
        /// there — null until the first successful create/sync.</summary>
        public int? AbisGroupId { get; set; }

        public long CreatedBy { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public long? ModifyBy { get; set; }
        public DateTimeOffset? ModifyAt { get; set; }
    }
}
