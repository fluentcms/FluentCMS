namespace FluentCMS.Api.Models;

public class UserAuthenticateRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}
