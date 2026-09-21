using System.Net;
using System.Net.Mail;

namespace MxfaceWebAPI.Services
{
    // Sends an alert email for unexpected (not specifically-handled) exceptions — mirrors the
    // legacy webapi.face ExceptionMailSend/SendExceptionAsync behavior. A failed send must never
    // change the caller's real API response, so failures here are swallowed (after logging).
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IHostEnvironment _environment;

        public EmailService(
            IConfiguration configuration,
            ILogger<EmailService> logger,
            IHttpContextAccessor httpContextAccessor,
            IHostEnvironment environment)
        {
            _configuration = configuration;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _environment = environment;
        }

        public async Task ExceptionMailSend(string message, Exception ex)
        {
            MailMessage? mailMessage = null;
            try
            {
                // This project authenticates via subscription-key (not JWT/claims) — ClientId is
                // resolved by APIAuthorizationFilterAttribute and stashed in HttpContext.Items,
                // same source BiometricControllerBase.ResolvedClientId reads from. There's no
                // separate per-user identity in this auth model, so no UserId here.
                var httpContext = _httpContextAccessor.HttpContext;
                var clientId = httpContext?.Items["ClientId"]?.ToString() ?? "N/A";

                var fromAddress = _configuration["ExceptionFromEmail:SmtpUsername"];
                var toAddress = _configuration["ExceptionFromEmail:ToAddress"];
                //if (string.IsNullOrWhiteSpace(fromAddress) || string.IsNullOrWhiteSpace(toAddress))
                //{
                //    _logger.LogWarning("ExceptionMailSend skipped for {Operation} — ExceptionFromEmail config is incomplete", message);
                //    return;
                //}

                mailMessage = new MailMessage
                {
                    From = new MailAddress(fromAddress),
                    Subject = "MxFace Exception: " + _environment.EnvironmentName,
                    Body = $"ClientId: {clientId}\n\nError: {message}\n\nMessage: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}"
                };
                mailMessage.To.Add(toAddress);

                await SendExceptionAsync(mailMessage).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Already logged inside SendExceptionAsync — swallow here so a failed alert can
                // never turn into a different outcome for the real API caller.
            }
            finally
            {
                mailMessage?.Dispose();
            }
        }

        private async Task SendExceptionAsync(MailMessage mailMessage)
        {
            using var smtpClient = new SmtpClient(
                _configuration["ExceptionFromEmail:SmtpServer"],
                Convert.ToInt32(_configuration["ExceptionFromEmail:SmtpPort"]));
            try
            {
                smtpClient.EnableSsl = _configuration["ExceptionFromEmail:EnableSsl"] != "false";
                smtpClient.UseDefaultCredentials = false;
                smtpClient.Credentials = new NetworkCredential(
                    _configuration["ExceptionFromEmail:SmtpUsername"],
                    _configuration["ExceptionFromEmail:SmtpPassword"]);
                smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
                await smtpClient.SendMailAsync(mailMessage).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // Deliberately NOT logging SmtpPassword here (the reference implementation this
                // was adapted from did) — never write credentials to logs, even for diagnostics.
                _logger.LogError(ex, "Failed to send exception alert email via {SmtpServer}:{SmtpPort} as {SmtpUsername}",
                    _configuration["ExceptionFromEmail:SmtpServer"],
                    _configuration["ExceptionFromEmail:SmtpPort"],
                    _configuration["ExceptionFromEmail:SmtpUsername"]);
                throw;
            }
        }
    }
}
