using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MxfaceWebAPI.Models;
using MxfaceWebAPI.Services;

namespace MxfaceWebAPI.Filters
{
    // Gate for subscription-key-authenticated biometric endpoints. Controllers using this must
    // also carry [AllowAnonymous] — otherwise the global JWT fallback policy (see
    // SecurityServiceExtensions.AddDefaultAuthorization) rejects the request before this filter
    // ever runs, and the caller gets the generic empty-body Bearer 401 instead of this one.
    public class APIAuthorizationFilterAttribute : ActionFilterAttribute
    {
        private const string SubscriptionKeyHeader = "subscriptionkey";
        private const string InvalidKeyMessage =
            "Access denied due to invalid subscription key or wrong API endpoint. Make sure to provide a valid key for an active subscription.";

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<APIAuthorizationFilterAttribute>>();
            var subscriptionKey = context.HttpContext.Request.Headers[SubscriptionKeyHeader].ToString();

            if (string.IsNullOrWhiteSpace(subscriptionKey))
            {
                logger.LogWarning("Rejected {Path}: missing {Header} header", context.HttpContext.Request.Path, SubscriptionKeyHeader);
                context.Result = Unauthorized();
                return;
            }

            var commonService = context.HttpContext.RequestServices.GetRequiredService<ICommonService>();
            var client = await commonService.GetClientByKeyAsync(subscriptionKey);

            if (client is null)
            {
                logger.LogWarning("Rejected {Path}: subscription key did not resolve to an active client", context.HttpContext.Request.Path);
                context.Result = Unauthorized();
                return;
            }

            context.HttpContext.Items["ClientId"] = client.ClientId;
            context.HttpContext.Items["SubscriptionKey"] = subscriptionKey;

            await next();
        }

        private static IActionResult Unauthorized()
        {
            return new JsonResult(new ApiErrorResponse
            {
                Code = StatusCodes.Status401Unauthorized,
                Error = InvalidKeyMessage
            })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
        }
    }
}
