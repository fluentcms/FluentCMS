namespace FluentCMS.Identity;

public class RoleClaim : IdentityRoleClaim<Guid>, IEntity
{
    [Key]
    public new Guid Id { get; set; }
}