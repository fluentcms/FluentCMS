namespace FluentCMS.Api.Plugins.Models.Identity;

public class UserClaim : IdentityUserClaim<Guid>, IEntity
{
    [Key]
    public new Guid Id { get; set; }
}
