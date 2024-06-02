namespace FluentCMS.Web.Api.Models;

public class UserLoginRequest
{
    [Required]
    public string Username { get; set; } = default!;

    [Required]
    public string Password { get; set; } = default!;

    public bool RememberMe { get; set; }
}
