namespace FluentCMS.Repositories.Abstractions;

public interface IPluginRepository : ISiteAssociatedRepository<Plugin>
{
    Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default);
}
