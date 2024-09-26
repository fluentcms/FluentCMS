namespace FluentCMS.Repositories.Abstractions;

public interface IPluginRepository : ISiteAssociatedRepository<Plugin>
{
    Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default);
    Task<Plugin> UpdateOrder(Guid pluginId, string section, int order, CancellationToken cancellationToken = default);
}
