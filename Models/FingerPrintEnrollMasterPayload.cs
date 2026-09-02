namespace MxfaceWebAPI.Models
{
    // The exact shape the ABIS master expects for FingerPrint Enroll's "data" payload — internal
    // only. FingerPrintController.Enroll builds this from the public FingerPrintEnrollRequest;
    // it's never bound via [FromBody] and never appears in Swagger.
    public class FingerPrintEnrollMasterPayload : CommonRequest
    {
        public string GroupName { get; set; } = string.Empty;       // [M]
        public object SysInfo { get; set; } = new();                 // [O] shape undocumented — send {}
        public DemographicsPayload Demographics { get; set; } = new(); // [O] referenceId = caller's externalId
        public FingerprintsPayload Fingerprints { get; set; } = new(); // [M]
    }
}
