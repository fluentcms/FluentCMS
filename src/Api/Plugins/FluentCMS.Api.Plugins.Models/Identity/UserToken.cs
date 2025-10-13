namespace FluentCMS.Api.Plugins.Models.Identity;

public class UserToken : IdentityUserToken<Guid>, IEntity
{
    public Guid Id { get; set; }
}
