using MxfaceWebAPI.Models;

namespace MxfaceWebAPI.Data
{
    public class FaceIdentityMapDataAccess : IFaceIdentityMapDataAccess
    {
        // Group columns are aliased with a "group" prefix so ExecuteSelectAsync's reflective
        // mapper doesn't confuse them with the identity's own createdat/modifyat. Metadata is a
        // lateral "limit 1" so a missing unique index on face_identity_metadata can never
        // duplicate identity rows.
        private const string SelectSql = """
            select m.faceidentityid, m.clientid, m.externalid, m.createdat, m.modifyat,
                   g.id as groupid, g.groupname, g.createdat as groupcreatedat, g.modifyat as groupmodifyat,
                   md.metadata::text as metadata
            from faceclient_db.face_identity_map m
            left join faceclient_db.groups g on g.id = m.groupid and g.clientid = m.clientid
            left join lateral (
                select x.metadata from faceclient_db.face_identity_metadata x
                where x.faceidentityid = m.faceidentityid and x.clientid = m.clientid and x.isactive = 1
                limit 1) md on true
            """;

        private readonly IPostgresHelper _postgresHelper;

        public FaceIdentityMapDataAccess(IPostgresHelper postgresHelper)
        {
            _postgresHelper = postgresHelper;
        }

        public async Task<FaceIdentityMap?> GetByFaceIdentityIdAsync(long faceIdentityId, long clientId)
        {
            var sql = $"{SelectSql} where m.faceidentityid = $1 and m.clientid = $2 and m.isdeleted = 0";
            var results = await _postgresHelper.ExecuteSelectAsync<FaceIdentityMap>(sql, faceIdentityId, clientId);
            return results.FirstOrDefault();
        }

        public async Task<FaceIdentityMap?> GetByExternalIdAsync(string externalId, long clientId)
        {
            var sql = $"{SelectSql} where m.externalid = $1 and m.clientid = $2 and m.isdeleted = 0";
            var results = await _postgresHelper.ExecuteSelectAsync<FaceIdentityMap>(sql, externalId, clientId);
            return results.FirstOrDefault();
        }

        // groupId null = every identity of the client. Ordered by faceidentityid so offset/limit
        // paging is stable between calls.
        public Task<List<FaceIdentityMap>> GetListAsync(long clientId, long? groupId, int offset, int limit)
        {
            var sql = $"""
                {SelectSql}
                where m.clientid = $1 and m.isdeleted = 0 and ($2::bigint is null or m.groupid = $2::bigint)
                order by m.faceidentityid
                offset $3 limit $4
                """;
            return _postgresHelper.ExecuteSelectAsync<FaceIdentityMap>(sql, clientId, groupId, offset, limit);
        }

        public async Task<long> GetCountAsync(long clientId, long? groupId)
        {
            const string sql = """
                select count(*) from faceclient_db.face_identity_map
                where clientid = $1 and isdeleted = 0 and ($2::bigint is null or groupid = $2::bigint)
                """;
            return Convert.ToInt64(await _postgresHelper.ExecuteScalarAsync(sql, clientId, groupId));
        }

        public async Task<long> InsertAsync(long clientId, string externalId, long groupId, long? createdBy)
        {
            // uq_face_identity_map_client_external also covers soft-deleted rows, so a plain
            // insert would fail on re-enroll after delete. On conflict: a deleted row is revived
            // with the new group; an active row keeps its group (ABIS took the enroll as an
            // Update of the existing identity) and only gets its modify stamp refreshed.
            const string sql = """
                insert into faceclient_db.face_identity_map (clientid, externalid, groupid, createdby, createdat)
                values ($1, $2, $3, $4, now())
                on conflict (clientid, externalid) do update
                set groupid = case when face_identity_map.isdeleted = 1 then excluded.groupid else face_identity_map.groupid end,
                    isdeleted = 0, modifyby = excluded.createdby, modifyat = now()
                returning faceidentityid
                """;

            return Convert.ToInt64(await _postgresHelper.ExecuteScalarAsync(sql, clientId, externalId, groupId, createdBy));
        }

        public async Task<bool> DeleteAsync(long faceIdentityId, long clientId, long? deletedBy)
        {
            // Soft delete — rows are kept, only flagged: isdeleted = 1 on the identity and
            // isactive = 0 on its metadata, in one statement (data-modifying CTE) so both change
            // together. Every read in this class filters isdeleted = 0.
            const string sql = """
                with deactivated_metadata as (
                    update faceclient_db.face_identity_metadata
                    set isactive = 0, updatedby = $3::bigint, updateddate = now()
                    where faceidentityid = $1 and clientid = $2 and isactive = 1)
                update faceclient_db.face_identity_map
                set isdeleted = 1, modifyby = $3::bigint, modifyat = now()
                where faceidentityid = $1 and clientid = $2 and isdeleted = 0
                """;

            var affected = await _postgresHelper.ExecuteNonQueryAsync(sql, faceIdentityId, clientId, deletedBy);
            return affected > 0;
        }

        public async Task<string?> GetMetadataAsync(long faceIdentityId, long clientId)
        {
            const string sql = """
                select metadata::text from faceclient_db.face_identity_metadata
                where faceidentityid = $1 and clientid = $2 and isactive = 1
                """;

            return await _postgresHelper.ExecuteScalarAsync(sql, faceIdentityId, clientId) as string;
        }

        public async Task<string?> AddMetadataAsync(long faceIdentityId, long clientId, string metadataJson, long? createdBy)
        {
            // Inserts only when the identity has no active metadata row yet — no row returned
            // means metadata already existed. Doesn't depend on a unique index existing; the
            // partial unique index uq_face_identity_metadata_client_identity (if created) is
            // still the last guard against two concurrent adds both inserting.
            const string sql = """
                insert into faceclient_db.face_identity_metadata (faceidentityid, clientid, metadata, createdby, createddate, isactive)
                select $1::bigint, $2::bigint, $3::jsonb, $4::bigint, now(), 1
                where not exists (
                    select 1 from faceclient_db.face_identity_metadata
                    where faceidentityid = $1::bigint and clientid = $2::bigint and isactive = 1)
                returning metadata::text
                """;

            return await _postgresHelper.ExecuteScalarAsync(sql, faceIdentityId, clientId, metadataJson, createdBy) as string;
        }

        public async Task<string?> UpdateMetadataAsync(long faceIdentityId, long clientId, string metadataJson, long? updatedBy)
        {
            const string sql = """
                update faceclient_db.face_identity_metadata
                set metadata = $3::jsonb, updatedby = $4, updateddate = now()
                where faceidentityid = $1 and clientid = $2 and isactive = 1
                returning metadata::text
                """;

            return await _postgresHelper.ExecuteScalarAsync(sql, faceIdentityId, clientId, metadataJson, updatedBy) as string;
        }
    }
}
