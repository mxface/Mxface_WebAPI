namespace MxfaceWebAPI.Models
{
    // One side (probe or gallery) of a Face Match/Verify request — same {bioData:[...]} wrapper
    // Enroll/Search use, just under the "faces" key instead of "fingerprints".
    public class FaceProbeGalleryPayload
    {
        public FingerprintsPayload Faces { get; set; } = new();
    }
}
