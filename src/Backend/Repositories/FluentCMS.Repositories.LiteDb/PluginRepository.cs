namespace FluentCMS.Repositories.LiteDb;

public class PluginRepository : SiteAssociatedRepository<Plugin>, IPluginRepository
{
    public PluginRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

    public async Task<IEnumerable<Plugin>> GetByColumnId(Guid columnId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.Query().Where(x => x.ColumnId == columnId).ToListAsync();
    }
}
