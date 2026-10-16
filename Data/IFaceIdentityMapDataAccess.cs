using MxfaceWebAPI.Models;

namespace MxfaceWebAPI.Data
{
    // Database-access layer for faceclient_db.face_identity_map. Every method is scoped by
    // clientId so one client can never read another client's identities.
    public interface IFaceIdentityMapDataAccess
    {
        Task<FaceIdentityMap?> GetByFaceIdentityIdAsync(long faceIdentityId, long clientId);
        Task<FaceIdentityMap?> GetByExternalIdAsync(string externalId, long clientId);
        // groupId null = all identities of the client.
        Task<List<FaceIdentityMap>> GetListAsync(long clientId, long? groupId, int offset, int limit);
        Task<long> GetCountAsync(long clientId, long? groupId);
        // Called only after ABIS enroll succeeded. Returns the identity's faceidentityid —
        // the existing one when (clientid, externalid) already has a row.
        Task<long> InsertAsync(long clientId, string externalId, long groupId, long? createdBy);
        // Soft delete: isdeleted = 1 on the identity, isactive = 0 on its metadata. Returns false
        // when no non-deleted identity row matched.
        Task<bool> DeleteAsync(long faceIdentityId, long clientId, long? deletedBy);

        // faceclient_db.face_identity_metadata — metadata is passed and returned as JSON text.
        Task<string?> GetMetadataAsync(long faceIdentityId, long clientId);
        // Returns null when the identity already has active metadata (nothing inserted).
        Task<string?> AddMetadataAsync(long faceIdentityId, long clientId, string metadataJson, long? createdBy);
        // Returns null when the identity has no active metadata to update.
        Task<string?> UpdateMetadataAsync(long faceIdentityId, long clientId, string metadataJson, long? updatedBy);
    }
}
