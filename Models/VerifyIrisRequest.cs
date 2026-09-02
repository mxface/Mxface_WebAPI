namespace MxfaceWebAPI.Models
{
    public class VerifyIrisRequest : CommonRequest
    {
        public string Iris1 { get; set; }
        public string Iris2 { get; set; }
    }
}
