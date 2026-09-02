namespace MxfaceWebAPI.Models
{
    // One candidate from a Search/Identify result — mapped from the master's
    // data.candidates[].referenceId / data.candidates[].analytics.confidence.
    public class MatchResultEntry
    {
        public string? ExternalId { get; set; }
        public float? MatchingScore { get; set; }
    }
}
