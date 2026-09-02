namespace MxfaceWebAPI.Models
{
    public class IrisDeleteRequest : CommonRequest
    {
        public string ExternalId { get; set; }
        public string Group { get; set; }
    }
}
