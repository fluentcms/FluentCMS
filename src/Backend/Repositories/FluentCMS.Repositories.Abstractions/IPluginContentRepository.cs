namespace FluentCMS.Repositories.Abstractions;

public interface IPluginContentRepository : ISiteAssociatedRepository<PluginContent>
{
    Task<IEnumerable<PluginContent>> GetByPluginId(Guid pluginId, CancellationToken cancellationToken = default);
}
