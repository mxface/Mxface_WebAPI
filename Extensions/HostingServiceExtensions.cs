namespace MxfaceWebAPI.Extensions
{
    public static class HostingServiceExtensions
    {
        public static WebApplicationBuilder ConfigureMxfaceHosting(this WebApplicationBuilder builder)
        {
            builder.Services.AddHsts(options =>
            {
                options.Preload = true;
                options.IncludeSubDomains = true;
                options.MaxAge = TimeSpan.FromDays(365);
            });

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB
            });

            return builder;
        }
    }
}
