namespace MxfaceWebAPI.Models
{
    // Maps to faceclient_db.transactions — one row per biometric API call.
    public class TransactionLogEntry
    {
        public long ClientId { get; init; }
        public string ReqId { get; init; } = string.Empty;
        public string RequestPayload { get; init; } = string.Empty;   // reqpaylod
        public DateTime RequestTimestamp { get; init; }               // rects
        public string ResponsePayload { get; init; } = string.Empty;  // respayload
        public string Error { get; init; } = string.Empty;
        public DateTime ResponseTimestamp { get; init; }               // rests
        public string ErrorPoint { get; init; } = string.Empty;
        public int FeatureType { get; init; }
        public int QuotaCount { get; init; }
        public string ClientIp { get; init; } = string.Empty;
        public long CreatedBy { get; init; }
    }
}
