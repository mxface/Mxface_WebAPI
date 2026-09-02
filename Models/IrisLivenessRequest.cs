namespace MxfaceWebAPI.Models
{
    public class IrisLivenessRequest : CommonRequest
    {
        public string IrisData { get; set; } = default!;
        public bool DetectLiveness { get; set; } = true;
        public byte QualityThreshold { get; set; } = 39;
        public int PassScore { get; set; } = 50;
    }
}
