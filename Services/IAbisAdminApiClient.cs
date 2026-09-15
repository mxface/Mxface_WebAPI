using MxfaceWebAPI.Models.AdminApi;

namespace MxfaceWebAPI.Services
{
    public interface IAbisAdminApiClient
    {
        Task<AdminApiResponse<EncryptionKeyInfo>> FetchEncryptionKeyAsync(CancellationToken cancellationToken = default);
        Task<AdminApiResponse<LoginResult>> LoginAsync(CancellationToken cancellationToken = default);
        Task<AdminApiResponse<AdminClientResponse>> CreateClientAsync(AdminCreateClientRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Full update of a client (e.g. renaming the org). Since 02-Sep-2026 PUT is merge-patch
        /// (a key not sent is left alone) — for groups-only changes, prefer
        /// <see cref="UpdateClientGroupsAsync"/> instead of resending every field.
        /// </summary>
        Task<AdminApiResponse<AdminClientResponse>> UpdateClientAsync(int clientNumericId, AdminCreateClientRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Add/update/remove specific groups on a client without touching any other field —
        /// relies on PUT /admin/clients/{id}'s merge-patch semantics (since 02-Sep-2026).
        /// </summary>
        Task<AdminApiResponse<AdminClientResponse>> UpdateClientGroupsAsync(int clientNumericId, GroupsDelta delta,string clientCode, CancellationToken cancellationToken = default);
    }
}
