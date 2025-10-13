namespace FluentCMS.Api.Plugins.IdentityManagement.Models;

public class UserClaim : IdentityUserClaim<Guid>, IEntity
{
    [Key]
    public new Guid Id { get; set; }
}
