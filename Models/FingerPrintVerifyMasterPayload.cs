namespace MxfaceWebAPI.Models
{
    // The exact shape the ABIS master expects for FingerPrint Match/Verify's "data" payload —
    // internal only. FingerPrintController.Verify builds this from the public
    // VerifyFingerPrintsRequest; it's never bound via [FromBody] and never appears in Swagger.
    // "probe" is the first image, "gallery" is the second — matched 1:1 against each other.
    public class FingerPrintVerifyMasterPayload : CommonRequest
    {
        public object SysInfo { get; set; } = new();
        public FingerprintProbeGalleryPayload Probe { get; set; } = new();
        public FingerprintProbeGalleryPayload Gallery { get; set; } = new();
    }
}
