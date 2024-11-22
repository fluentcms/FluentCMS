namespace FluentCMS.Repositories.EFCore;

public class PluginContentRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<PluginContent, PluginContentModel>(dbContext, mapper, apiExecutionContext), IPluginContentRepository
{
    public async Task<IEnumerable<PluginContent>> GetByPluginId(Guid pluginId, CancellationToken cancellationToken = default)
    {
        var dbEntities = await DbContext.PluginContents.Where(x => x.PluginId == pluginId).ToListAsync(cancellationToken);
        return Mapper.Map<IEnumerable<PluginContent>>(dbEntities);
    }
}
