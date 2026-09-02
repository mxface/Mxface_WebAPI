namespace MxfaceWebAPI.Models
{
    // One side (probe or gallery) of a FingerPrint Match/Verify request — reuses the same
    // {bioData:[...]} wrapper Enroll uses.
    public class FingerprintProbeGalleryPayload
    {
        public FingerprintsPayload Fingerprints { get; set; } = new();
    }
}
