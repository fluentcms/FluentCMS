
namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository : SiteAssociatedRepository<Role>, IRoleRepository
{
    public RoleRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

    public async Task<Role> GetByNameAndSiteId(Guid siteId, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.SiteId == siteId && x.Name == name).FirstOrDefaultAsync(cancellationToken);
    }
}
