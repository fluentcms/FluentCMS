namespace FluentCMS.Api.Core.Models.Identity;

public class UserLogin : IdentityUserLogin<Guid>, IEntity
{
    public Guid Id { get; set; }
}
