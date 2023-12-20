namespace FluentCMS.Web.Api.Models;

public class UserLoginRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}
