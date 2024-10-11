namespace FluentCMS.Repositories.RavenDB;

public class PluginContentRepository : SiteAssociatedRepository<PluginContent>, IPluginContentRepository
{
    public PluginContentRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : base(RavenDbContext, apiExecutionContext)
    {
    }

    public async Task<IEnumerable<PluginContent>> GetByPluginId(Guid pluginId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var qres = await session.Query<PluginContent>().Where(x => x.PluginId == pluginId).ToListAsync(cancellationToken);

            return qres.AsEnumerable();
        }
    }
}
