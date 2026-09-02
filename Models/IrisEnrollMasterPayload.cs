namespace MxfaceWebAPI.Models
{
    // The exact shape the ABIS master expects for Iris Enroll's "data" payload — internal
    // only. IrisController.Enroll builds this from the public IrisEnrollRequest; it's never
    // bound via [FromBody] and never appears in Swagger.
    public class IrisEnrollMasterPayload : CommonRequest
    {
        public string GroupName { get; set; } = string.Empty;          // [M]
        public object SysInfo { get; set; } = new();                    // [O] shape undocumented — send {}
        public DemographicsPayload Demographics { get; set; } = new();  // [O] referenceId = caller's externalId
        public FingerprintsPayload Irises { get; set; } = new();        // [M] — reuses the generic {bioData:[...]} wrapper
    }
}
