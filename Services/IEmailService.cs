namespace MxfaceWebAPI.Services
{
    public interface IEmailService
    {
        Task ExceptionMailSend(string message, Exception ex);
    }
}
