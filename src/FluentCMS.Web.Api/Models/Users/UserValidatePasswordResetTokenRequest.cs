using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models.Users;

public class UserValidatePasswordResetTokenRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    public string NewPassword { get; set; } = string.Empty;
}
