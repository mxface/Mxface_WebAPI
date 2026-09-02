using MxfaceWebAPI.Data;
using Npgsql;

namespace MxfaceWebAPI.Extensions
{
    public static class DatabaseServiceExtensions
    {
        public static IServiceCollection AddMxfaceNpgsqlDataSource(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("FaceClientDb");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Connection string 'ConnectionStrings:FaceClientDb' is not configured.");
            }

            services.AddNpgsqlDataSource(connectionString);
            services.AddScoped<IPostgresHelper, PostgresHelper>();

            return services;
        }
    }
}
