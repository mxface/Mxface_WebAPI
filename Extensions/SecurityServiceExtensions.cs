using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace MxfaceWebAPI.Extensions
{
    public static class SecurityServiceExtensions
    {
        public static IServiceCollection AddJwtAuthentication(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            var jwtKey = configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
            {
                throw new InvalidOperationException(
                    "Jwt:Key is missing or too short. Configure a signing key of at least 32 bytes via " +
                    "User Secrets, environment variables (Jwt__Key), or a secret store before starting the app.");
            }

            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = !environment.IsDevelopment();
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = configuration["Jwt:Issuer"],
                        ValidateAudience = true,
                        ValidAudience = configuration["Jwt:Audience"],
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(1)
                    };
                });

            return services;
        }

        public static IServiceCollection AddDefaultAuthorization(this IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                // Every endpoint requires an authenticated caller unless explicitly marked [AllowAnonymous].
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });

            return services;
        }

        public static WebApplication UseSecurityHeaders(this WebApplication app)
        {
            // Swagger UI and the landing page need to load same-origin JS/CSS; a plain API response never does.
            var contentSecurityPolicy = app.Environment.IsDevelopment()
                ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'"
                : "default-src 'none'; frame-ancestors 'none'";

            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Content-Security-Policy"] = contentSecurityPolicy;
                context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                await next();
            });

            return app;
        }
    }
}
