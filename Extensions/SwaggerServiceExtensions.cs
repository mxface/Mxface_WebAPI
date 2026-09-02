using Microsoft.OpenApi.Models;

namespace MxfaceWebAPI.Extensions
{
    public static class SwaggerServiceExtensions
    {
        public static IServiceCollection AddMxfaceSwagger(this IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("Face API V3", new OpenApiInfo
                {
                    Title = "MXFace - Face API V3",
                    Version = "v3"
                });
                options.SwaggerDoc("Identity V3", new OpenApiInfo
                {
                    Title = "MXFace - Identity API V3 (1-to-n search)",
                    Version = "v3"
                });
                options.SwaggerDoc("BiometricAPI", new OpenApiInfo
                {
                    Title = "Biometric API V1",
                    Version = "v1"
                });

                options.DocInclusionPredicate((docName, apiDesc) => apiDesc.GroupName == docName);

                // Surfaces each action's route Name (e.g. [HttpPost(Name = "VerifyFingerPrint")]) as
                // its Swagger operation id — otherwise Swashbuckle falls back to its own generated id
                // and the route Name never shows up anywhere in the UI.
                options.CustomOperationIds(apiDesc => apiDesc.ActionDescriptor.AttributeRouteInfo?.Name);

                // APIAuthorizationFilterAttribute reads this header directly off HttpContext —
                // Swashbuckle has no way to discover that on its own, so without this definition
                // there's no "subscriptionkey" field anywhere in Swagger UI to type it into.
                var subscriptionKeyScheme = new OpenApiSecurityScheme
                {
                    Name = "subscriptionkey",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Description = "Subscription key required by the biometric (Iris/FingerPrint) endpoints.",
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "SubscriptionKey"
                    }
                };

                options.AddSecurityDefinition("SubscriptionKey", subscriptionKeyScheme);
                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    { subscriptionKeyScheme, new List<string>() }
                });
            });

            return services;
        }

        public static WebApplication UseMxfaceSwagger(this WebApplication app)
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/Face API V3/swagger.json", "Face API V3");
                options.SwaggerEndpoint("/swagger/Identity V3/swagger.json", "Identity V3 (1-to-n)");
                options.SwaggerEndpoint("/swagger/BiometricAPI/swagger.json", "Biometric API");

                // Public-facing docs: hide the standalone "Schemas" panel at the bottom of the
                // page (internal model shapes). Each endpoint's own request/response body is
                // still shown when expanded - only the separate all-models list is hidden.
                options.DefaultModelsExpandDepth(-1);
            });

            return app;
        }
    }
}
