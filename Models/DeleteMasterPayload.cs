namespace MxfaceWebAPI.Models
{
    // The exact shape the ABIS master expects for Delete's "data" payload — internal only.
    // Shared by both FingerPrintController.Delete and IrisController.Delete since the master's
    // Delete schema has no modality-specific field at all — it removes the identity/record from
    // a group by referenceId, regardless of which modality's REST endpoint triggered it.
    public class DeleteMasterPayload : CommonRequest
    {
        public object SysInfo { get; set; } = new();
        public string GroupName { get; set; } = string.Empty;
        public List<string> ReferenceIds { get; set; } = new();
    }
}
