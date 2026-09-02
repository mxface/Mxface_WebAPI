namespace MxfaceWebAPI.Models.Request.Group
{
    // Placeholder shape based on the ABIS Admin API guide's group PUT payload (groupName
    // mandatory, description/isDefault the only editable fields) — the real Group API isn't
    // implemented yet, this just unblocks the build for GroupController's stubbed actions.
    public class CreateGroupRequest
    {
        public string GroupName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool? IsDefault { get; set; }
    }
}
