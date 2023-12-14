namespace FluentCMS.Api.Models;

public class UserCreateRequest
{
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public ICollection<Guid> RoleIds { get; set; } = [];
}
