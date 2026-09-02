namespace MxfaceWebAPI.Models
{
    public class FingerPrintDeleteRequest : CommonRequest
    {
        public string ExternalId { get; set; }
        public string Group { get; set; }
    }
}
