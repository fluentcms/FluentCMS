namespace FluentCMS.Web.Api.Models;

public class UserValidatePasswordResetTokenRequest
{
    [Required]
    public string Token { get; set; } = default!;

    [Required]
    public string Email { get; set; } = default!;

    [Required]
    public string NewPassword { get; set; } = default!;

    [Required]
    [Compare(nameof(NewPassword))]
    public string NewPasswordConfirm { get; set; } = default!;
}
