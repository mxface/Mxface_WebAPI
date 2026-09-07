using Npgsql;
using MxfaceWebAPI.Models;

namespace MxfaceWebAPI.Data
{
    // Thin wrapper over NpgsqlDataSource: builds a command from sql + positional parameters
    // ($1, $2, ...), runs it, and disposes it. All Postgres-specific plumbing lives here so
    // services stay free of Npgsql types.
    public class PostgresHelper : IPostgresHelper
    {
        // treqpaylod/treqts/trespayload/terror/trests are nullable and intentionally left out —
        // purpose not yet confirmed. id is left out too: no confirmed default/identity on that
        // column yet, so this INSERT relies on the DB supplying it.
        private const string LogTransactionSql = """
            insert into faceclient_db.transactions
                (clientid, reqid, reqpaylod, rects, respayload, error, rests, errorpoint,
                 featuretype, quotacount, clientip, createdby, createdat)
            values
                ($1, $2, $3::jsonb, $4, $5::jsonb, $6, $7, $8, $9, $10, $11, $12, $13)
            """;

        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<PostgresHelper> _logger;

        public PostgresHelper(NpgsqlDataSource dataSource, ILogger<PostgresHelper> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public Task LogTransactionAsync(TransactionLogEntry entry)
        {
            return ExecuteNonQueryAsync(
                LogTransactionSql,
                entry.ClientId,
                entry.ReqId,
                entry.RequestPayload,
                entry.RequestTimestamp,
                entry.ResponsePayload,
                entry.Error,
                entry.ResponseTimestamp,
                entry.ErrorPoint,
                entry.FeatureType,
                entry.QuotaCount,
                entry.ClientIp,
                entry.CreatedBy,
                DateTime.UtcNow);
        }

        public async Task<object?> ExecuteScalarAsync(string sql, params object[] parameters)
        {
            try
            {
                await using var command = CreateCommand(sql, parameters);
                return await command.ExecuteScalarAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Postgres command failed.");
                throw;
            }
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, params object[] parameters)
        {
            try
            {
                await using var command = CreateCommand(sql, parameters);
                return await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Postgres command failed.");
                throw;
            }
        }

        private NpgsqlCommand CreateCommand(string sql, object[] parameters)
        {
            var command = _dataSource.CreateCommand(sql);
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter);
            }

            return command;
        }
    }
}
