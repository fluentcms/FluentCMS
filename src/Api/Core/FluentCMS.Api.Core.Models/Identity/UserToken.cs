namespace FluentCMS.Api.Core.Models.Identity;

public class UserToken : IdentityUserToken<Guid>, IEntity
{
    public Guid Id { get; set; }
}
