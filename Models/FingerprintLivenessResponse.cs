namespace MxfaceWebAPI.Models
{
    public class FingerprintLivenessResponse : BiomatricBaseResponse
    {
        public bool? IsLive { get; set; }
        public float? LivenessScore { get; set; }
        public int? ExtractedQuality { get; set; }
        public string? BiometricStatus { get; set; }
    }
}
