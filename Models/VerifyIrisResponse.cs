namespace MxfaceWebAPI.Models
{
    public class VerifyIrisResponse:BiomatricBaseResponse
    {
        public float? MatchingScore { get; set; }
        public int? Matched { get; set; }
    }
}
