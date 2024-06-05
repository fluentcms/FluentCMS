namespace FluentCMS.Web.Api.Models;

public class UserSendPasswordResetTokenRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = default!;
}
