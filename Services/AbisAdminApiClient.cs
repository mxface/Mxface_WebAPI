using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MxfaceWebAPI.Configuration;
using MxfaceWebAPI.Crypto;
using MxfaceWebAPI.Models.AdminApi;
using MxfaceWebAPI.Serialization;

namespace MxfaceWebAPI.Services
{
    /// <summary>
    /// Client for the ABIS Admin API's encrypted-envelope protocol (Group/Client/Role/User
    /// provisioning) — a completely separate system from the biometric gRPC master used
    /// everywhere else in this project. Every call (after the bootstrap key fetch) is wrapped in
    /// AES-256-GCM + RSA-OAEP, authenticated via HttpOnly session cookies, and state-changing
    /// calls must echo the ABIS_CSRF cookie as the X-CSRF-Token header.
    ///
    /// Ported from the live-verified reference client at
    /// E:\MxFaceNewWork\MaxFaceMagIdClientAPI\MaxFaceMagIdClientAPI\Http\AbisApiClient.cs — trimmed
    /// to just what Group work needs (FetchEncryptionKey/Login/CreateClient/UpdateClient); Role/User/
    /// SubscriptionKey calls weren't ported since nothing in this project uses them yet.
    ///
    /// Session state (cookies, current encryption key) lives for the lifetime of this instance —
    /// registered as a Singleton so login only happens once and the session is reused across
    /// requests, matching the reference's single-instance-for-the-chain design.
    /// </summary>
    public sealed class AbisAdminApiClient : IAbisAdminApiClient, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly CookieContainer _cookies;
        private readonly AbisAdminApiSettings _settings;
        private readonly ILogger<AbisAdminApiClient> _logger;
        private readonly SemaphoreSlim _loginLock = new(1, 1);
        private EncryptionKeyInfo? _currentKey;

        public AbisAdminApiClient(IConfiguration configuration, ILogger<AbisAdminApiClient> logger)
        {
            _settings = configuration.GetSection("AbisAdminApi").Get<AbisAdminApiSettings>() ?? new AbisAdminApiSettings();
            _logger = logger;
            _cookies = new CookieContainer();

            var handler = new HttpClientHandler
            {
                CookieContainer = _cookies,
                UseCookies = true
            };

            if (_settings.BypassTlsValidation)
            {
                // Internal/test host with a self-signed certificate. Never enable this against a
                // production endpoint — it disables all TLS chain validation for this client.
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds)
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<AdminApiResponse<EncryptionKeyInfo>> FetchEncryptionKeyAsync(CancellationToken cancellationToken = default)
        {
            const string path = "public/encryption-key";
            try
            {
                using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return AdminApiResponse<EncryptionKeyInfo>.Failure((int)response.StatusCode, $"Failed to fetch encryption key ({response.ReasonPhrase}).", raw);
                }

                var key = JsonSerializer.Deserialize<EncryptionKeyInfo>(raw, AdminJson.Options);
                if (key is null || string.IsNullOrWhiteSpace(key.PublicKey) || string.IsNullOrWhiteSpace(key.KeyId))
                {
                    return AdminApiResponse<EncryptionKeyInfo>.Failure((int)response.StatusCode, "encryption-key response was missing publicKey/keyId.", raw);
                }

                _currentKey = key;
                return AdminApiResponse<EncryptionKeyInfo>.Success((int)response.StatusCode, key, raw);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch Admin API encryption key");
                return AdminApiResponse<EncryptionKeyInfo>.Failure(0, ex.Message);
            }
        }

        public async Task<AdminApiResponse<LoginResult>> LoginAsync(CancellationToken cancellationToken = default)
        {
            var payload = new
            {
                ClientId = _settings.LoginClientId,
                Username = _settings.Username.ToLowerInvariant(),
                Password = _settings.Password
            };

            var response = await SendEncryptedAsync<LoginResult>(HttpMethod.Post, _settings.LoginPath, payload, requiresCsrf: false, cancellationToken)
                .ConfigureAwait(false);

            // auth-lib can return 200 OK with { ec, em } instead of throwing — that is still a failed login.
            if (response.IsSuccess && response.Data?.Ec is not null)
            {
                return AdminApiResponse<LoginResult>.Failure(response.StatusCode, response.Data.Em ?? $"Login failed (ec={response.Data.Ec}).", response.RawContent);
            }

            return response;
        }

        public Task<AdminApiResponse<AdminClientResponse>> CreateClientAsync(AdminCreateClientRequest request, CancellationToken cancellationToken = default) =>
            SendEncryptedAsync<AdminClientResponse>(HttpMethod.Post, "admin/clients", request, requiresCsrf: true, cancellationToken);

        public Task<AdminApiResponse<AdminClientResponse>> UpdateClientAsync(int clientNumericId, AdminCreateClientRequest request, CancellationToken cancellationToken = default) =>
            SendEncryptedAsync<AdminClientResponse>(HttpMethod.Put, $"admin/clients/{clientNumericId}", request, requiresCsrf: true, cancellationToken);

        public Task<AdminApiResponse<AdminClientResponse>> UpdateClientGroupsAsync(int clientNumericId, GroupsDelta delta,string Clientcode, CancellationToken cancellationToken = default) =>
            SendEncryptedAsync<AdminClientResponse>(HttpMethod.Put, $"admin/clients/{Clientcode}", new ClientGroupsPatchRequest { Groups = delta }, requiresCsrf: true, cancellationToken);

        // Nothing else in this app calls LoginAsync explicitly — this instance is a singleton with
        // its own cookie jar, so the first CSRF-protected call after startup (or after the session
        // expires) needs to establish it on demand rather than failing and asking the caller to log
        // in first. _loginLock serializes concurrent callers so a cold-start burst of requests
        // triggers one login, not one per request.
        private async Task<AdminApiResponse<TResponse>?> EnsureLoggedInAsync<TResponse>(CancellationToken cancellationToken)
        {
            if (ReadCsrfToken() is not null)
            {
                return null;
            }

            await _loginLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Re-check — another caller may have already logged in while this one was waiting.
                if (ReadCsrfToken() is not null)
                {
                    return null;
                }

                var loginResult = await LoginAsync(cancellationToken).ConfigureAwait(false);
                if (!loginResult.IsSuccess)
                {
                    _logger.LogError("Auto-login before a CSRF-protected Admin API call failed: {Error}", loginResult.ErrorMessage);
                    return AdminApiResponse<TResponse>.Failure(loginResult.StatusCode, loginResult.ErrorMessage ?? "Auto-login before this endpoint failed.");
                }

                return null;
            }
            finally
            {
                _loginLock.Release();
            }
        }

        private async Task<AdminApiResponse<TResponse>> SendEncryptedAsync<TResponse>(
            HttpMethod method, string path, object payload, bool requiresCsrf, CancellationToken cancellationToken)
        {
            Exception? lastException = null;

            for (var attempt = 0; attempt <= _settings.RetryCount; attempt++)
            {
                var keyResult = _currentKey is not null
                    ? AdminApiResponse<EncryptionKeyInfo>.Success(200, _currentKey, null)
                    : await FetchEncryptionKeyAsync(cancellationToken).ConfigureAwait(false);

                if (!keyResult.IsSuccess || keyResult.Data is null)
                {
                    return AdminApiResponse<TResponse>.Failure(keyResult.StatusCode, keyResult.ErrorMessage ?? "Unable to obtain an encryption key.");
                }

                // Every attempt gets a fresh envelope: new IV, new AES key, new RequestId/timestamp —
                // reusing one across retries would trip the server's replay check.
                var envelope = AdminEnvelopeCrypto.Encrypt(payload, keyResult.Data.PublicKey, keyResult.Data.KeyId, out var aesKey, out _);

                using var request = new HttpRequestMessage(method, path)
                {
                    Content = new StringContent(envelope.ToJsonString(AdminJson.Options), Encoding.UTF8, "application/json")
                };

                if (requiresCsrf)
                {
                    var loginFailure = await EnsureLoggedInAsync<TResponse>(cancellationToken).ConfigureAwait(false);
                    if (loginFailure is not null)
                    {
                        return loginFailure;
                    }

                    var csrf = ReadCsrfToken();
                    if (string.IsNullOrEmpty(csrf))
                    {
                        return AdminApiResponse<TResponse>.Failure(0, "No ABIS_CSRF cookie present even after a successful login.");
                    }
                    request.Headers.Add("X-CSRF-Token", csrf);
                }

                try
                {
                    using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                    JsonDocument? document = null;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            document = JsonDocument.Parse(raw);
                        }
                    }
                    catch (JsonException)
                    {
                        // Non-JSON body (rare) — fall through and treat as a plain failure below.
                    }

                    using (document)
                    {
                        var root = document?.RootElement ?? default;
                        var isObject = document is not null && root.ValueKind == JsonValueKind.Object;

                        // errorCode 42010 = encryption key expired: response carries a fresh key, unencrypted.
                        // Rotate and retry the whole call once with a brand-new envelope.
                        if (isObject && attempt < _settings.RetryCount &&
                            root.TryGetProperty("errorCode", out var errorCodeEl) &&
                            errorCodeEl.ValueKind == JsonValueKind.Number && errorCodeEl.GetInt32() == 42010 &&
                            root.TryGetProperty("newPublicKey", out var newKeyEl) &&
                            root.TryGetProperty("newKeyId", out var newKeyIdEl))
                        {
                            _currentKey = new EncryptionKeyInfo
                            {
                                PublicKey = newKeyEl.GetString() ?? string.Empty,
                                KeyId = newKeyIdEl.GetString() ?? string.Empty
                            };
                            continue;
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            var message = ExtractErrorMessage(isObject ? root : null) ?? $"Request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).";
                            return AdminApiResponse<TResponse>.Failure((int)response.StatusCode, message, raw);
                        }

                        if (!isObject || !root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.String)
                        {
                            var message = ExtractErrorMessage(isObject ? root : null) ?? "Response did not contain an encrypted 'data' field.";
                            return AdminApiResponse<TResponse>.Failure((int)response.StatusCode, message, raw);
                        }

                        var decryptedJson = AdminEnvelopeCrypto.Decrypt(dataEl.GetString()!, aesKey);
                        var data = JsonSerializer.Deserialize<TResponse>(decryptedJson, AdminJson.Options);
                        return AdminApiResponse<TResponse>.Success((int)response.StatusCode, data, decryptedJson);
                    }
                }
                catch (Exception ex) when (attempt < _settings.RetryCount && ex is HttpRequestException or TaskCanceledException)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Admin API call to {Path} failed — retrying (attempt {Attempt}/{RetryCount})", path, attempt + 1, _settings.RetryCount);
                    await Task.Delay(_settings.RetryDelayMilliseconds * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Admin API call to {Path} failed unexpectedly", path);
                    return AdminApiResponse<TResponse>.Failure(0, ex.Message);
                }
            }

            return AdminApiResponse<TResponse>.Failure(0, lastException?.Message ?? "Request failed after retries.");
        }

        private static string? ExtractErrorMessage(JsonElement? root)
        {
            if (root is not { ValueKind: JsonValueKind.Object } element)
            {
                return null;
            }

            if (element.TryGetProperty("em", out var em) && em.ValueKind == JsonValueKind.String)
            {
                return em.GetString();
            }

            if (element.TryGetProperty("errorMessage", out var errorMessage) && errorMessage.ValueKind == JsonValueKind.String)
            {
                return errorMessage.GetString();
            }

            if (element.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }

            if (element.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }

            return null;
        }

        private string? ReadCsrfToken()
        {
            foreach (Cookie cookie in _cookies.GetCookies(_httpClient.BaseAddress!))
            {
                if (string.Equals(cookie.Name, "ABIS_CSRF", StringComparison.Ordinal))
                {
                    return cookie.Value;
                }
            }

            return null;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _loginLock.Dispose();
        }
    }
}
