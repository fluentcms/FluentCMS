namespace FluentCMS.Api.Core.Models.Identity;

public class UserToken : IdentityUserToken<Guid>, IEntity
{
    public Guid Id { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;   // Manual revocation flag
}
