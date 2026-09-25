namespace MxfaceWebAPI.Models
{
    // The exact shape the ABIS master expects for Face Match/Verify's "data" payload — internal
    // only. FaceController.Verify builds this from the public VerifyFaces request; it's never
    // bound via [FromBody] and never appears in Swagger. "probe" is the first image, "gallery" is
    // the second — matched 1:1, same convention as FingerPrintVerifyMasterPayload.
    public class FaceVerifyMasterPayload : CommonRequest
    {
        public object SysInfo { get; set; } = new();
        public FaceProbeGalleryPayload Probe { get; set; } = new();
        public FaceProbeGalleryPayload Gallery { get; set; } = new();
    }
}
