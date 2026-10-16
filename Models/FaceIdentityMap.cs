namespace MxfaceWebAPI.Models
{
    // Row model for faceclient_db.face_identity_map joined with its group from
    // faceclient_db.groups — only identifiers, no biometric data (faces live in ABIS). Every
    // property matches its column name / alias in FaceIdentityMapDataAccess case-insensitively.
    public class FaceIdentityMap
    {
        public long FaceIdentityId { get; set; }
        public long ClientId { get; set; }
        public string ExternalId { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ModifyAt { get; set; }

        // Group columns come from a LEFT JOIN, so all are null when groupid is null or the
        // group row no longer exists (groupid has no foreign key).
        public long? GroupId { get; set; }
        public string? GroupName { get; set; }
        public DateTimeOffset? GroupCreatedAt { get; set; }
        public DateTimeOffset? GroupModifyAt { get; set; }

        // Active row's JSON from faceclient_db.face_identity_metadata, null when none.
        public string? Metadata { get; set; }
    }
}
