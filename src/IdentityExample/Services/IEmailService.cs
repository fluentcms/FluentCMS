namespace IdentityExample.Services;

public interface IEmailService
{
    Task SendEmailConfirmationAsync(string email, string userName, string confirmationToken);
    Task SendPasswordResetAsync(string email, string userName, string resetToken);
    Task SendWelcomeEmailAsync(string email, string userName);
}
