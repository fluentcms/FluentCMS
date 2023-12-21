namespace FluentCMS.Web.Api.Models;

public class SetupStartRequest
{
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
    public string Email { get; set; } = default!;
}
