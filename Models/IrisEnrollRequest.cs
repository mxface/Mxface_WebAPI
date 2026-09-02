namespace MxfaceWebAPI.Models
{
    public class IrisEnrollRequest : CommonRequest
    {
        public string Iris { get; set; }
        public string ExternalId { get; set; }
        public string Group { get; set; }
    }
}
