namespace MxfaceWebAPI.Models.Request.FaceIdentity
{
    public class UpdateGroupRequest
    {
        public List<int> AddGroupIds { get; set; }
        public List<int> DeleteGroupIds { get; set; }
    }
}
