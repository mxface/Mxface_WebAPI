namespace MxfaceWebAPI.Models
{
    // Marker base for every biometric request model — shared type constraint for
    // BiometricControllerBase.CallAsync. Ver/ReqId/Ts used to live here as caller-overridable
    // fields; they're now always generated internally (see ClientApiEnvelopeFactory), so callers
    // no longer supply or see them.
    public abstract class CommonRequest
    {
    }
}
