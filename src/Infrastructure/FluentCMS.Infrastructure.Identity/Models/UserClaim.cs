namespace FluentCMS.Infrastructure.Identity.Models;

public class UserClaim : IdentityUserClaim<Guid>, IEntity
{
    [Key]
    public new Guid Id { get; set; }
}
