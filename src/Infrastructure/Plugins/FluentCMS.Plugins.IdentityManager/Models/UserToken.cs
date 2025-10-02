namespace FluentCMS.Plugins.IdentityManager.Models;

public class UserToken : IdentityUserToken<Guid>, IEntity
{
    public Guid Id { get; set; }
}
