namespace MxfaceWebAPI.Models.Response.Group
{
    // Matches the documented Group API v3 "Get / List by Name" response shape — always a wrapped
    // array, even for a single exact-name match or when listing every group.
    public class GroupListResponse
    {
        public List<GroupResponse> Groups { get; set; } = new();
    }
}
