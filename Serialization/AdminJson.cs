using System.Text.Json;
using System.Text.Json.Serialization;

namespace MxfaceWebAPI.Serialization
{
    // Serializer options for the ABIS Admin API (Group/Client/Role/User provisioning) — separate
    // from any biometric-master JSON handling elsewhere in this project.
    public static class AdminJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
