using MxfaceWebAPI.Data;
using MxfaceWebAPI.Models;
using System.Collections.Concurrent;
using static Grpc.Core.Metadata;

namespace MxfaceWebAPI.Services
{
    // Resolves a caller's subscription key to the owning client via
    // faceclient_db.user_api_keys -> client_users -> clients.
    public class CommonService : ICommonService
    {
        // user_api_keys.status: 0 = Active, 1/2 = inactive/revoked (no DB comment documents this —
        // confirm with the real key-issuance flow before relying on it elsewhere).
        private const short ActiveKeyStatus = 1;

        private readonly IPostgresHelper _postgresHelper;
        private readonly ILogger<CommonService> _logger;

        public CommonService(IPostgresHelper postgresHelper, ILogger<CommonService> logger)
        {
            _postgresHelper = postgresHelper;
            _logger = logger;
        }

        public async Task<ClientDetails?> GetClientByKeyAsync(string subscriptionKey)
        {
            if (string.IsNullOrWhiteSpace(subscriptionKey))
            {
                return null;
            }

            const string sql = """
                select c.id
                from faceclient_db.user_api_keys k
                join faceclient_db.client_users u on u.id = k.userid
                join faceclient_db.clients c on c.id = u.clientid
                where k.secretkey = $1
                  and k.status = $2
                  and (k.expiresat is null or k.expiresat > now())
                """;

            try
            {
                var clientId = await _postgresHelper.ExecuteScalarAsync(sql, subscriptionKey, ActiveKeyStatus);
                return clientId is long id ? new ClientDetails { ClientId = id } : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve client for subscription key lookup");
                throw;
            }
        }

        #region For remove-Key
        public int RemoveKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                //clientList = new ConcurrentDictionary<string, Entity.TrimmedEntity.Client>();
                return 0;
            }
            else
            {
                //var client = GetClientByKey(key);
                //var cl = new ClientAPILimit();
                //clientLimit.TryRemove(client.ClientId, out cl);
                //clientList.TryRemove(key, out client);
                //return client.ClientId;
                return 0;
            }
        }
        #endregion

        #region For remove-Key
        //public Entity.TrimmedEntity.Client GetClientByKey(string key)
        //{
        //    Entity.TrimmedEntity.Client client;
        //    if (!clientList.TryGetValue(key, out client))
        //    {
        //        // Try primary AccessKey lookup
        //        client = _clientService.GetAuthInfo(key);
        //        if (client == null)
        //        {
        //            // Fallback: try PendingAccessKey lookup (dual-key during grace period)
        //            client = _clientService.GetAuthInfoByPendingKey(key);
        //        }
        //        if (client != null)
        //            clientList.TryAdd(key, client);
        //    }
        //    lock (_entriesLock)
        //    {
        //        return client;
        //    }
        //}
        #endregion
    }
}
