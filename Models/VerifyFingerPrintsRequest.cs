namespace MxfaceWebAPI.Models
{
    public class VerifyFingerPrintsRequest : CommonRequest
    {
        public string FingerPrint1 { get; set; }
        public string FingerPrint2 { get; set; }
    }
}
