using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MxfaceWebAPI.Serialization;

namespace MxfaceWebAPI.Crypto
{
    /// <summary>
    /// Builds and opens the AES-256-GCM + RSA-OAEP envelope the ABIS Admin API (Group/Client/Role/
    /// User provisioning, port 8881 in the guide, live host below) uses — a completely separate
    /// protocol from the biometric gRPC master (abis_client.proto) used everywhere else in this
    /// project. Ported from the live-verified reference client at
    /// E:\MxFaceNewWork\MaxFaceMagIdClientAPI\MaxFaceMagIdClientAPI\Crypto\EnvelopeCrypto.cs.
    ///
    /// Wire format (per ABIS_API_GUIDE's envelope diagram):
    ///   data = Base64( IV(12 bytes) || ciphertext || tag(16 bytes) ), AES-256-GCM, no padding.
    ///   Key  = Base64( RSA-OAEP-SHA256( AES key ) ) using the server's public key — always 344 chars.
    ///   Envelope = { data, Key, Id (=keyId), RequestId, timestamp, ver:"2.0", enc:1 }.
    ///   The plaintext payload gets "_ts" and "_RequestId" embedded before encryption, matching
    ///   the outer timestamp/RequestId exactly (the server compares inner vs outer as a replay defence).
    ///   timestamp is Unix epoch MILLISECONDS — seconds triggers errorCode 42003 "Request timestamp
    ///   is stale or in the future" (confirmed against the live server by the reference client).
    /// </summary>
    public static class AdminEnvelopeCrypto
    {
        private const int IvSizeBytes = 12;
        private const int TagSizeBytes = 16;

        public static JsonObject Encrypt(object payload, string publicKeyBase64, string keyId, out byte[] aesKey, out string plaintextJson)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var requestId = Guid.NewGuid().ToString();

            var payloadNode = JsonSerializer.SerializeToNode(payload, AdminJson.Options) as JsonObject ?? new JsonObject();
            payloadNode["_ts"] = timestamp;
            payloadNode["_RequestId"] = requestId;

            plaintextJson = payloadNode.ToJsonString(AdminJson.Options);
            var plaintext = Encoding.UTF8.GetBytes(plaintextJson);

            aesKey = RandomNumberGenerator.GetBytes(32);
            var iv = RandomNumberGenerator.GetBytes(IvSizeBytes);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSizeBytes];

            using (var aesGcm = new AesGcm(aesKey, TagSizeBytes))
            {
                aesGcm.Encrypt(iv, plaintext, ciphertext, tag);
            }

            var combined = new byte[IvSizeBytes + ciphertext.Length + TagSizeBytes];
            Buffer.BlockCopy(iv, 0, combined, 0, IvSizeBytes);
            Buffer.BlockCopy(ciphertext, 0, combined, IvSizeBytes, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, combined, IvSizeBytes + ciphertext.Length, TagSizeBytes);

            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            var wrappedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);

            return new JsonObject
            {
                ["data"] = Convert.ToBase64String(combined),
                ["Key"] = Convert.ToBase64String(wrappedKey),
                ["Id"] = keyId,
                ["RequestId"] = requestId,
                ["timestamp"] = timestamp,
                ["ver"] = "2.0",
                ["enc"] = 1
            };
        }

        public static string Decrypt(string base64Data, byte[] aesKey)
        {
            var combined = Convert.FromBase64String(base64Data);
            if (combined.Length < IvSizeBytes + TagSizeBytes)
            {
                throw new CryptographicException("Encrypted envelope is shorter than IV + tag.");
            }

            var iv = combined.AsSpan(0, IvSizeBytes);
            var tag = combined.AsSpan(combined.Length - TagSizeBytes, TagSizeBytes);
            var ciphertext = combined.AsSpan(IvSizeBytes, combined.Length - IvSizeBytes - TagSizeBytes);
            var plaintext = new byte[ciphertext.Length];

            using (var aesGcm = new AesGcm(aesKey, TagSizeBytes))
            {
                aesGcm.Decrypt(iv, ciphertext, tag, plaintext);
            }

            return Encoding.UTF8.GetString(plaintext);
        }
    }
}
