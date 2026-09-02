using MxfaceWebAPI.Models;

namespace MxfaceWebAPI.Services
{
    public interface ICommonService
    {
        // Returns null when the subscription key doesn't resolve to an active, unexpired client.
        Task<ClientDetails?> GetClientByKeyAsync(string subscriptionKey);
    }
}
