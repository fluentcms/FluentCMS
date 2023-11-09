namespace FluentCMS.Entities.Identity;

public class SignInResult
{
    public virtual Guid UserId { get; set; }
    public virtual List<Guid> RoleIds { get; set; } = [];
    public virtual string Token { get; set; } = string.Empty;
    public virtual string RefreshToken { get; set; } = string.Empty;
    public virtual DateTime Expiry { get; set; }
}