namespace MxfaceWebAPI.Extensions
{
    public static class CorsServiceExtensions
    {
        public static IServiceCollection AddMxfaceCors(this IServiceCollection services, IConfiguration configuration)
        {
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

            services.AddCors(options =>
            {
                options.AddPolicy("DefaultCorsPolicy", policy =>
                {
                    if (allowedOrigins.Length > 0)
                    {
                        policy.WithOrigins(allowedOrigins)
                              .AllowAnyHeader()
                              .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                              .AllowCredentials();
                    }
                    // No origins configured -> no cross-origin browser access is permitted (safe default).
                });
            });

            return services;
        }
    }
}
