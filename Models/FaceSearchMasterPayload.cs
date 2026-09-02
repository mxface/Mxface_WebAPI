namespace MxfaceWebAPI.Models
{
    // The exact shape the ABIS master expects for Face Search/Identify's "data" payload —
    // internal only. Used by FaceIdentityController.Enroll's pre-flight duplicate-identity check
    // (search the target group for a similar existing face before enrolling a new one).
    public class FaceSearchMasterPayload : CommonRequest
    {
        public object SysInfo { get; set; } = new();
        public string GroupName { get; set; } = string.Empty;
        public FingerprintsPayload Faces { get; set; } = new();
    }
}
