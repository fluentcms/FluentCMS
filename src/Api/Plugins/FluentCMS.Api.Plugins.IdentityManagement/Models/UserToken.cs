namespace FluentCMS.Api.Plugins.IdentityManagement.Models;

public class UserToken : IdentityUserToken<Guid>, IEntity
{
    public Guid Id { get; set; }
}
