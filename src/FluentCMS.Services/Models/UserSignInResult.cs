namespace FluentCMS.Services.Models;

public class UserSignInResult
{
    public Guid UserId { get; set; }
    public List<Guid> RoleIds { get; set; } = [];
    public string? Token { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime Expiry { get; set; }
}