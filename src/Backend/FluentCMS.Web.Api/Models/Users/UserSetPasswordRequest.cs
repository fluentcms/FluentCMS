namespace FluentCMS.Web.Api.Models;

public class UserSetPasswordRequest
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public string Password { get; set; } = default!;
}
