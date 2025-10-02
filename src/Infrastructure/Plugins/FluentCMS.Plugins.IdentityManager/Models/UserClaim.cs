namespace FluentCMS.Plugins.IdentityManager.Models;

public class UserClaim : IdentityUserClaim<Guid>, IEntity
{
    [Key]
    public new Guid Id { get; set; }
}
