namespace FluentCMS.Api.Core.Models.Identity;

public class RoleClaim : IdentityRoleClaim<Guid>, IEntity
{
    public new Guid Id { get; set; }
}
