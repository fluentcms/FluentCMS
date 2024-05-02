namespace FluentCMS.Web.Api.Models;

public class UserChangePasswordRequest
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public string OldPassword { get; set; } = default!;

    [Required]
    public string NewPassword { get; set; } = default!;
}
