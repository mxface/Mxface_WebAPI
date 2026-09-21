using System.Text.Json.Serialization;

namespace MxfaceWebAPI.Models
{
    public class FaceAnalytics
    {
        // Old live API used PascalCase for this one key specifically; every other field here is
        // already camelCase by default.
        [JsonPropertyName("EstimatedAge")]
        public string? EstimatedAge { get; set; }
        public string? Gender { get; set; }
        public string? Emotion { get; set; }
        public int? Confidence { get; set; }
    }
}
