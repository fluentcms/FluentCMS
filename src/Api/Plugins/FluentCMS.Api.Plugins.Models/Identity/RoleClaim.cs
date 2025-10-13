namespace FluentCMS.Api.Plugins.Models.Identity;

public class RoleClaim : IdentityRoleClaim<Guid>, IEntity
{
    [Key]
    public new Guid Id { get; set; }
}
