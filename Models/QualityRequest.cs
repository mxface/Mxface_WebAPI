using System.Text.Json.Serialization;

namespace MxfaceWebAPI.Models
{
    public class QualityRequest
    {
        [JsonPropertyName("encoded_image")]
        public string EncodedImage { get; set; }

        public double? MinimumDistanceBetweenEyeThreshold { get; set; }

        public bool? Attributes { get; set; }
    }
}
