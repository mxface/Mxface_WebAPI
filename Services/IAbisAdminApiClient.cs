using MxfaceWebAPI.Models.AdminApi;

namespace MxfaceWebAPI.Services
{
    public interface IAbisAdminApiClient
    {
        Task<AdminApiResponse<EncryptionKeyInfo>> FetchEncryptionKeyAsync(CancellationToken cancellationToken = default);
        Task<AdminApiResponse<LoginResult>> LoginAsync(CancellationToken cancellationToken = default);
        Task<AdminApiResponse<AdminClientResponse>> CreateClientAsync(AdminCreateClientRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Also how groups are created/updated/deleted — send the client's full current field set plus
        /// the complete desired groups array; any existing group id you omit from Groups gets deleted.
        /// </summary>
        Task<AdminApiResponse<AdminClientResponse>> UpdateClientAsync(int clientNumericId, AdminCreateClientRequest request, CancellationToken cancellationToken = default);
    }
}
