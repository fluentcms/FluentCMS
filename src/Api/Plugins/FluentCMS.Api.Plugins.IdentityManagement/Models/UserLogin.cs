namespace FluentCMS.Api.Plugins.IdentityManagement.Models;

public class UserLogin : IdentityUserLogin<Guid>, IEntity
{
    [Key]
    public Guid Id { get; set; }
}
