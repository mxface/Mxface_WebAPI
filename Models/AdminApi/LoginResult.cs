namespace MxfaceWebAPI.Models.AdminApi
{
    public sealed class LoginResult
    {
        public string? ClientId { get; set; }
        public int ExpiresIn { get; set; }
        public int RefreshExpiresIn { get; set; }
        public string? TokenType { get; set; }
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
        public string? UserType { get; set; }
        public string? Timezone { get; set; }
        public bool IsFirstLogin { get; set; }

        // Present only when the encryption key needed rotating.
        public string? NewPublicKey { get; set; }
        public string? NewKeyId { get; set; }

        // auth-lib can return HTTP 200 with this failure shape instead of throwing — see guide.
        // Confirmed live shape: { "status": "Failed", "em": "...", "ts": "...", "ec": "-2004" } — ec is a string.
        public string? Status { get; set; }
        public string? Ec { get; set; }
        public string? Em { get; set; }
    }
}
