namespace MxfaceWebAPI.Models
{
    public class VerifyFingerPrintsResponse: BiomatricBaseResponse
    {
        public float? MatchingScore { get; set; }
        public int? Matched { get; set; }
    }
}
