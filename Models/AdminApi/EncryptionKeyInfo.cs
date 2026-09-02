namespace MxfaceWebAPI.Models.AdminApi
{
    public sealed class EncryptionKeyInfo
    {
        public string PublicKey { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
    }
}
