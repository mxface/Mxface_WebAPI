namespace MxfaceWebAPI.Models
{
    // The exact shape the ABIS master expects for Face Enroll's "data" payload — internal only.
    // FaceIdentityController.Enroll builds this from the public CreateFaceIdentityRequest; it's
    // never bound via [FromBody] and never appears in Swagger.
    // TODO: unconfirmed against a real master sample (only Finger/Iris Enroll schemas have been
    // confirmed live so far) — "faces" is a guess following the fingerprints/irises naming
    // pattern. GroupName only carries the FIRST of model.GroupIds — multi-group Enroll is
    // explicitly out of scope for this pass (see project memory).
    public class FaceEnrollMasterPayload : CommonRequest
    {
        public string GroupName { get; set; } = string.Empty;
        public object SysInfo { get; set; } = new();
        public DemographicsPayload Demographics { get; set; } = new();
        public FingerprintsPayload Faces { get; set; } = new();
    }
}
