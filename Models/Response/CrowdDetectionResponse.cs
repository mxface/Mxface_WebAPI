using Newtonsoft.Json;
using System.Collections.Generic;

namespace MxfaceWebAPI.Models.Response
{
    public class CrowdDetectionResponse : BiomatricBaseResponse
    {
        [JsonProperty("base64_image")]
        public string encoded_image { get; set; }

        [JsonProperty("people_count")]
        public int peopleCount { get; set; }

        [JsonProperty("scores")]
        public List<double> score { get; set; }
    }

    public class CrowdDetectionRequest
    {
        public string encoded_image { get; set; }
        public bool returnImage { get; set; }
    }
}
