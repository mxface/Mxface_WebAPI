namespace MxfaceWebAPI.Models
{
    // One side (probe or gallery) of an Iris Match/Verify request — reuses the same
    // {bioData:[...]} wrapper Enroll uses.
    // TODO: the "irises" key name is unconfirmed for Verify specifically — chosen to match
    // Iris Enroll's already-live-confirmed key, per the user's own uncertainty ("i think iris
    // and finger time both are same object but object name i think is different"). Correct if
    // a live test shows the master expects something else (e.g. singular "iris").
    public class IrisProbeGalleryPayload
    {
        public FingerprintsPayload irises { get; set; } = new();
    }
}
