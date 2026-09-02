namespace MxfaceWebAPI.Models
{
    public class BiometricEnrollResponse : BiomatricBaseResponse
    {
        // The master doesn't generate its own identity id on Enroll — it echoes back the
        // referenceId we sent (see FingerPrintController.Enroll's demographics.referenceId)
        // inside the response's "data.referenceId". Populated explicitly in
        // BiometricControllerBase.CallAsync (not via [JsonPropertyName], which would also rename
        // this field in OUR OWN output and break the public REST contract). Kept as IdentityId
        // on the public REST contract.
        public string? IdentityId { get; set; }
    }
}
