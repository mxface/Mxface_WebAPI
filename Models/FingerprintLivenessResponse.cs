namespace MxfaceWebAPI.Models
{
    public class FingerprintLivenessResponse : BiomatricBaseResponse
    {
        public bool? IsLive { get; set; }
        public float? LivenessScore { get; set; }
    }
}
