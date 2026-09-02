namespace MxfaceWebAPI.Models
{
    public class FingerPrintEnrollRequest : CommonRequest
    {
        public string FingerPrint { get; set; }
        public string ExternalId { get; set; }
        public string Group { get; set; }
    }
}
