namespace FluentCMS.Plugins.IdentityManager.Models;

public class UserLogin : IdentityUserLogin<Guid>, IEntity
{
    [Key]
    public Guid Id { get; set; }
}
