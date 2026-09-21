namespace MxfaceWebAPI.Models.Response.Group
{
    // Matches the documented Group API v3 contract exactly — groupId/groupName/createdDate/
    // updatedDate only. description/isDefault are tracked internally (Models/Group.cs, for the
    // ABIS sync) but deliberately not part of this public response shape.
    public class GroupResponse
    {
        public int? GroupId { get; set; }
        public string GroupName { get; set; }
        public DateTimeOffset CreatedDate { get; set; }
        public DateTimeOffset UpdatedDate { get; set; }
    }
}
