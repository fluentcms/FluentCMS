namespace FluentCMS.Api.Core.Models.Identity;

public class UserClaim : IdentityUserClaim<Guid>, IEntity
{
    public new Guid Id { get; set; }
}
