namespace FluentCMS.Repositories.LiteDb;

public class RoleRepository : AuditableEntityRepository<Role>, IRoleRepository
{
    public RoleRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

}
