namespace MxfaceWebAPI.Configuration
{
    // Bound from appsettings.json's "AbisAdminApi" section — this project's convention (unlike
    // the reference client, which reads App.config/ConfigurationManager).
    public sealed class AbisAdminApiSettings
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string LoginPath { get; set; } = "/user/internal-login";

        /// <summary>Tenant clientId you log in UNDER (not the client you provision/manage).</summary>
        public string LoginClientId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 30;
        public int RetryCount { get; set; } = 3;
        public int RetryDelayMilliseconds { get; set; } = 500;

        /// <summary>true = accept the server's TLS certificate without chain validation. Only for
        /// internal/test hosts with a self-signed cert — never in production.</summary>
        public bool BypassTlsValidation { get; set; }
    }
}
