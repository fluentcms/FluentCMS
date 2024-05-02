namespace FluentCMS.Web.Api.Models;

public class UserValidatePasswordResetTokenRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = default!;

    [Required]
    public string Token { get; set; } = default!;


    [Required]
    public string NewPassword { get; set; } = default!;
}
