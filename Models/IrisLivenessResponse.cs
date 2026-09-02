namespace MxfaceWebAPI.Models
{
    public class IrisLivenessResponse : BiomatricBaseResponse
    {
        public bool? IsLive { get; set; }
        public float? LivenessScore { get; set; }
    }
}
