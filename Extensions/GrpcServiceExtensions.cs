using MxfaceWebAPI.Grpc;
using MxfaceWebAPI.Grpc.AbisClient;

namespace MxfaceWebAPI.Extensions
{
    public static class GrpcServiceExtensions
    {
        // No real address is configured yet, so this fallback keeps the app from crashing at
        // startup (new Uri("") throws) if GrpcServices:ClientApiService is missing. Calls just
        // fail at request time with a normal RpcException until a real address is set.
        private const string PlaceholderAddress = "https://localhost:5001";

        public static IServiceCollection AddMxfaceGrpcClients(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddGrpcClient<ClientApiService.ClientApiServiceClient>(options =>
            {
                options.Address = new Uri(configuration["GrpcServices:ClientApiService"] is { Length: > 0 } clientApiAddress
                    ? clientApiAddress
                    : PlaceholderAddress);
            });

            services.AddSingleton<IClientApiEnvelopeFactory, ClientApiEnvelopeFactory>();

            return services;
        }
    }
}
