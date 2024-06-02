namespace FluentCMS.Web.Api.Models;

public class AccountChangePasswordRequest
{
    [Required]
    public string OldPassword { get; set; } = default!;

    [Required]
    public string NewPassword { get; set; } = default!;

    [Required]
    [Compare(nameof(NewPassword))]
    public string NewPasswordConfirm { get; set; } = default!;
}
