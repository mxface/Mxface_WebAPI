namespace MxfaceWebAPI.Models
{
    // The exact shape the ABIS master expects for Iris Search/Identify's "data" payload —
    // internal only. IrisController.Search builds this from the public IrisSearchRequest; it's
    // never bound via [FromBody] and never appears in Swagger.
    // "referenceId" (probe by enrolled identity) and "gallery.referenceIds" (restrict search)
    // are optional per the master's schema — omitted here since this only covers the core case
    // (probe by fresh image, search the full group).
    public class IrisSearchMasterPayload : CommonRequest
    {
        public object SysInfo { get; set; } = new();
        public string GroupName { get; set; } = string.Empty;
        public FingerprintsPayload Irises { get; set; } = new();
    }
}
