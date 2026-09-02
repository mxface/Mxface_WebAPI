using System.Text.Json;

namespace MxfaceWebAPI.Models
{
    public class QualityResponse : BiomatricBaseResponse
    {
        public JsonElement? Result { get; set; }
    }
}
