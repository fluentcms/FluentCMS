namespace FluentCMS.Web.Api.Models;

public class UserLoginRequest
{
    [Required]
    public required string Username { get; set; }

    [Required]
    public required string Password { get; set; }
}
