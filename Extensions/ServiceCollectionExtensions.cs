using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.RateLimiting;
using MxfaceWebAPI.Services;

namespace MxfaceWebAPI.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMxfaceCommonServices(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();
            services.AddScoped<ICommonService, CommonService>();
            services.AddScoped<IEmailService, EmailService>();
            services.AddSingleton<IAbisAdminApiClient, AbisAdminApiClient>();

            return services;
        }

        public static IServiceCollection AddApiVersioningSetup(this IServiceCollection services)
        {
            services
                .AddApiVersioning(options =>
                {
                    options.DefaultApiVersion = new ApiVersion(3.0);
                    options.AssumeDefaultVersionWhenUnspecified = true;
                    options.ReportApiVersions = true;
                    options.ApiVersionReader = new UrlSegmentApiVersionReader();
                })
                .AddMvc()
                .AddApiExplorer(options =>
                {
                    options.GroupNameFormat = "'v'VVV";
                    options.SubstituteApiVersionInUrl = true;
                });

            return services;
        }

        public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 100,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
            });

            return services;
        }
    }
}
