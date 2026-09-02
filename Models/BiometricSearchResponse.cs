namespace MxfaceWebAPI.Models
{
    public class BiometricSearchResponse : BiomatricBaseResponse
    {
        public List<MatchResultEntry>? MatchResult { get; set; }
    }
}
