namespace FluentCMS.Web.Api.Models;

public class UserLoginResponse
{
    public Guid UserId { get; set; }
    public List<Guid> RoleIds { get; set; } = [];
    public required string Token { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
