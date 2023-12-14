namespace FluentCMS.Api.Models;

public class UserAuthenticateDto
{
    public Guid UserId { get; set; }
    public List<Guid> RoleIds { get; set; } = [];
    public required string Token { get; set; }
}
