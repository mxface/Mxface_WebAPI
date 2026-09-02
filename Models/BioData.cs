namespace MxfaceWebAPI.Models
{
    // One biometric sample record, matching the master's real "bioData" schema. Internal-only —
    // never bound via [FromBody], never appears in Swagger.
    public class BioData
    {
        public string Format { get; set; } = string.Empty;   // [M] e.g. "RAW", "FIR", "IIR", "FID", "JPEG"
        public string Version { get; set; } = "0";  // [M]
        public int? Wd { get; set; }                          // [M when Format == "RAW"]
        public int? Ht { get; set; }                          // [M when Format == "RAW"]
        public string Pos { get; set; } = "Unknown";                     // [O] e.g. "LeftThumb"
        public string Data { get; set; } = string.Empty;     // [M] base64-encoded biometric bytes
        public int? Qty { get; set; }                         // [O] quality score 0-100
        public double? Nfiq2 { get; set; }                    // [O] NFIQ2 score 0-100 (fingerprint)
    }
}
