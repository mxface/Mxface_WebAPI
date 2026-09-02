namespace MxfaceWebAPI.Models
{
    public class FingerprintLivenessRequest : CommonRequest
    {
        //public string FingerPrintImage { get; set; }

        public string FingerPrintData { get; set; }
        public bool DetectLiveness { get; set; } = true;
        public byte QualityThreshold { get; set; } = 39;
        public int PassScore { get; set; } = 50;
    }
}
