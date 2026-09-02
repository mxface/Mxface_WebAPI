namespace MxfaceWebAPI.Models
{
    public class FingerPrintSearchRequest : CommonRequest
    {
        //public string FingerPrint1 { get; set; }
        //public string FingerPrint2 { get; set; }
        //public int? GroupId { get; set; }
        //public float? Threshold { get; set; }

        public string FingerPrint { get; set; }
        public string Group { get; set; }
    }
}
