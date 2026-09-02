namespace MxfaceWebAPI.Models
{
    // The master's optional "demographics" object — currently the only field we populate is
    // referenceId, set to the caller's externalId so the master's record can be correlated back
    // to it later.
    public class DemographicsPayload
    {
        public string ReferenceId { get; set; } = string.Empty;
    }
}
