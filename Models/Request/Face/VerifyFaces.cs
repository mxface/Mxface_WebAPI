using System.Text.Json.Serialization;
using MxfaceWebAPI.Models.Request.Face;

namespace MxfaceWebAPI.Models.Request.Face
{
    public class VerifyFaces : CommonProperty
    {
        public string encoded_image1 { get; set; }
        public string encoded_image2 { get; set; }
        [JsonIgnore]
        public bool CompareAllFaces { get; set; }
        public int? QualityThreshold { get; set; } = null;
        public bool GetCroppedFace { get; set; }
        public bool returnConfidence { get; set; } = false;
        public string Algorithm { get; set; }
        public double? FAR { get; set; }

    }
}
