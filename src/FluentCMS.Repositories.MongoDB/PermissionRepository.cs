namespace FluentCMS.Repositories.MongoDB;

public class PermissionRepository(
    IMongoDBContext mongoDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<Permission>(mongoDbContext, authContext),
    IPermissionRepository
{
}
