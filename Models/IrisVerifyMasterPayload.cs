namespace MxfaceWebAPI.Models
{
    // The exact shape the ABIS master expects for Iris Match/Verify's "data" payload — internal
    // only. IrisController.Verify builds this from the public VerifyIrisRequest; it's never
    // bound via [FromBody] and never appears in Swagger.
    // "probe" is the first image, "gallery" is the second — matched 1:1 against each other.
    public class IrisVerifyMasterPayload : CommonRequest
    {
        public object SysInfo { get; set; } = new();
        public IrisProbeGalleryPayload Probe { get; set; } = new();
        public IrisProbeGalleryPayload Gallery { get; set; } = new();
    }
}
