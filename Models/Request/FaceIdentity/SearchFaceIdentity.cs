namespace MxfaceWebAPI.Models.Request.FaceIdentity
{
    public class SearchFaceIdentity
    {
        public List<int> GroupIds { get; set; }
        public string Encoded_Image { get; set; }
        public int? Limit { get; set; } = 1;
        public int? QualityThreshold { get; set; } = null;
        public int? MatchConfidence { get; set; }
        public bool returnConfidence { get; set; } = false;
    }
    public class NeuroSearchResult
    {
        public List<NeuroSearch> NeuroSearch { get; set; }
        public string NeuroStatusMessage { get; set; }
    }
    public class NeuroSearch
    {
        public string Id { get; set; }
        public int Score { get; set; }
    }
}
