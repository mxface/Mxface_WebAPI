namespace MxfaceWebAPI.Models.Response.Group
{
    public class GroupResponse
    {
        public int? GroupId { get; set; }
        public string GroupName { get; set; }
        public string? Description { get; set; }
        public bool IsDefault { get; set; }
        public DateTimeOffset CreatedDate { get; set; }
        public DateTimeOffset UpdatedDate { get; set; }
    }
}
