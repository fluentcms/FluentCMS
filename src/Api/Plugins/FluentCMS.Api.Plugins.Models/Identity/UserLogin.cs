namespace FluentCMS.Api.Plugins.Models.Identity;

public class UserLogin : IdentityUserLogin<Guid>, IEntity
{
    [Key]
    public Guid Id { get; set; }
}
