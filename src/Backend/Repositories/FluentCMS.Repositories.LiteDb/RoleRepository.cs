namespace FluentCMS.Repositories.LiteDb;

public class RoleRepository : SiteAssociatedRepository<Role>, IRoleRepository
{
    public RoleRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : base(liteDbContext, apiExecutionContext)
    {
    }

    public async Task<Role> GetByNameAndSiteId(Guid siteId, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.SiteId == siteId && x.Name == name).FirstOrDefaultAsync();
    }
}
