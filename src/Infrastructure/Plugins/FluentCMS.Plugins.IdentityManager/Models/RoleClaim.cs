namespace FluentCMS.Plugins.IdentityManager.Models;

public class RoleClaim : IdentityRoleClaim<Guid>, IEntity
{
    [Key]
    public new Guid Id { get; set; }
}
