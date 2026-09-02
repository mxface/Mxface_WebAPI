using MxfaceWebAPI.Models;

namespace MxfaceWebAPI.Data
{
    // Every raw Postgres call in the app goes through here — services never touch
    // NpgsqlDataSource/NpgsqlCommand directly, they only know SQL + parameters.
    public interface IPostgresHelper
    {
        Task<object?> ExecuteScalarAsync(string sql, params object[] parameters);
        Task<int> ExecuteNonQueryAsync(string sql, params object[] parameters);

        // Common insert point for every biometric API call's audit row (faceclient_db.transactions).
        Task LogTransactionAsync(TransactionLogEntry entry);
    }
}
