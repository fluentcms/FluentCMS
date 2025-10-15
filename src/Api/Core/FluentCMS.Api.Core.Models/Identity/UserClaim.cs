namespace FluentCMS.Api.Core.Models.Identity;

public class UserClaim : IdentityUserClaim<Guid>, IEntity
{
    [Key]
    public new Guid Id { get; set; }
}
