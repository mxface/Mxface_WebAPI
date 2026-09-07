using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MxfaceWebAPI.Grpc.AbisClient;
using MxfaceWebAPI.Models;

namespace MxfaceWebAPI.Grpc
{
    public interface IClientApiEnvelopeFactory
    {
        ClientApiRequest Create(int mode, CommonRequest request, string subscriptionKey);
    }

    // Builds the ClientApiRequest envelope the same way for every biometric endpoint, so each
    // controller action only supplies its mode + payload instead of repeating this boilerplate.
    // Ver/ReqId/Ts are taken from the caller's request when supplied; otherwise a default is filled in.
    public class ClientApiEnvelopeFactory : IClientApiEnvelopeFactory
    {
        // TODO: confirm the real default envelope version the master expects when the caller omits it.
        private const string DefaultVersion = "1.0";

        // The master's own responses use camelCase ("reqId", "ver", "ec", "em") — match that
        // convention for the outbound "data" payload instead of .NET's default PascalCase.
        // TODO: casing is confirmed from observed master responses; the actual field NAMES
        // (fingerPrint/externalId/group/etc.) are still unconfirmed against real master docs.
        private static readonly JsonSerializerOptions DataSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Optional fields (pos/qty/nfiq2/etc.) should be omitted entirely when unset, not sent
            // as explicit nulls — the master's schema marks them [O] Optional, and a strict
            // validator may treat a present-but-null field differently from an absent one.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ILogger<ClientApiEnvelopeFactory> _logger;

        public ClientApiEnvelopeFactory(ILogger<ClientApiEnvelopeFactory> logger)
        {
            _logger = logger;
        }

        public ClientApiRequest Create(int mode, CommonRequest request, string subscriptionKey)
        {
            if (string.IsNullOrWhiteSpace(subscriptionKey))
            {
                _logger.LogWarning("Caller subscription key is empty — this call to the master will carry no subscription key");
            }

            // Mandatory for the gRPC envelope per the proto — always generated internally now;
            // callers no longer supply (or see) these on the REST request body.
            var reqId = Guid.NewGuid().ToString();
            //var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            var ts = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",CultureInfo.InvariantCulture);

            // request is statically typed CommonRequest here — Serialize(request) would bind to
            // Serialize<CommonRequest> and only emit CommonRequest's own members, silently dropping
            // every derived field (FingerPrint1/Iris1/etc.). Passing the runtime type forces
            // reflection-based serialization of the actual object.
            // The master's real "data" schema requires reqId/ts duplicated inside data too, equal to
            // the envelope's own values — injected here rather than exposed on any request model, so
            // callers never supply (or see) them, same as the envelope-level fields above.
            //var dataNode = JsonSerializer.SerializeToNode(request, request.GetType(), DataSerializerOptions)!.AsObject();
            //dataNode["reqId"] = reqId;
            //dataNode["ts"] = ts;

            var dataNode = JsonSerializer.SerializeToNode(
                    request,
                    request.GetType(),
                    DataSerializerOptions
                )!.AsObject();

            // Remove existing reqId and ts
            dataNode.Remove("reqId");
            dataNode.Remove("ts");

            // Create new JSON object
            var orderedDataNode = new JsonObject();

            // Add ts and reqId first
            orderedDataNode["ts"] = ts;
            orderedDataNode["reqId"] = reqId;

            // Copy remaining properties using DeepClone()
            foreach (var property in dataNode)
            {
                orderedDataNode[property.Key] = property.Value?.DeepClone();
            }

            dataNode = orderedDataNode;

            // ClientApiRequest.Ver/ReqId/Ts/Enc/Mode already mirror the REST ApiRequest envelope
            // field-for-field (see abis_client.proto) — the master reconstructs the full
            // {ver, reqId, ts, enc, mode, data} envelope itself from those plus this Data field.
            // Data must be ONLY the inner payload (reqId/ts duplicated inside it, matching the
            // confirmed success-format sample) — wrapping it again in an outer {reqId, ts, data}
            // here would double up what the master already reconstructs.
            // TODO: enc=0 (plaintext) until session-key/HMAC signing is implemented — see
            // abis_client.proto. skey/ci/hmac are only meaningful on the encrypted path.
            var envelope = new ClientApiRequest
            {
                SubscriptionKey = subscriptionKey ?? string.Empty,
                Ver = DefaultVersion,
                ReqId = reqId,
                Ts = ts,
                Enc = 0,
                Mode = mode,
                Data = dataNode.ToJsonString(DataSerializerOptions)
            };

            return envelope;
        }
    }
}
