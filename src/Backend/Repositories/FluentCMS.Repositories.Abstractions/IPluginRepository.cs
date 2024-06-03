namespace FluentCMS.Repositories.Abstractions;

public interface IPluginRepository : IAuditableEntityRepository<Plugin>
{
    Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default);
}
