namespace FluentCMS.Repositories.Postgres.Repositories;

public class PluginContentRepository(PostgresDbContext context) : SiteAssociatedRepository<PluginContent>(context), IPluginContentRepository, IService
{

    public async Task<IEnumerable<PluginContent>> GetByPluginId(Guid pluginId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Table.Where(x => x.PluginId == pluginId).ToListAsync(cancellationToken: cancellationToken);
    }
}
