namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository : SiteAssociatedRepository<Role>, IRoleRepository
{
    public RoleRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : base(mongoDbContext, apiExecutionContext)
    {
    }

    public async Task<Role> GetByNameAndSiteId(Guid siteId, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.SiteId == siteId && x.Name == name).FirstOrDefaultAsync(cancellationToken);
    }
}
