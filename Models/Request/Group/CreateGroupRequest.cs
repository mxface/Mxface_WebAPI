namespace MxfaceWebAPI.Models.Request.Group
{
    // Reused for both Create and Update. groupName: required, max 30 characters, letters/digits/
    // spaces/\_[]()- only (see GroupService's validation) — set once at creation, never changed
    // afterward (confirmed with the ABIS API team: groupName can't be renamed at all, only
    // description/isDefault are updatable). Description/IsDefault are optional on both Create and
    // Update.
    public class CreateGroupRequest
    {
        public string GroupName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool? IsDefault { get; set; }
    }
}
