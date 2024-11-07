namespace FluentCMS.Repositories.EFCore;

public class PluginContentRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<PluginContent>(dbContext, apiExecutionContext), IPluginContentRepository
{
    public async Task<IEnumerable<PluginContent>> GetByPluginId(Guid pluginId, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(x => x.PluginId == pluginId).ToListAsync(cancellationToken);
    }
}
