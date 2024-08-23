
namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository : SiteAssociatedRepository<Role>, IRoleRepository
{
    public RoleRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

    public async Task<bool> RoleBySameNameIsExistInSite(Guid siteId, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isExist = await Collection.Find(x => x.SiteId == siteId && x.Name == name).AnyAsync(cancellationToken);
        return isExist;
    }
}
