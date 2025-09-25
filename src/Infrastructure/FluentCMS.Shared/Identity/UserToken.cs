namespace FluentCMS.Identity;

public class UserToken : IdentityUserToken<Guid>, IEntity
{
    public Guid Id { get; set; }
}