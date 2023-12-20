namespace FluentCMS.Web.Api.Models;

public class UserRegisterRequest
{
    public required string Email { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
}
