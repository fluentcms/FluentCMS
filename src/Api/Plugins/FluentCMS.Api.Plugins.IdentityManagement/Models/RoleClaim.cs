namespace FluentCMS.Api.Plugins.IdentityManagement.Models;

public class RoleClaim : IdentityRoleClaim<Guid>, IEntity
{
    [Key]
    public new Guid Id { get; set; }
}
