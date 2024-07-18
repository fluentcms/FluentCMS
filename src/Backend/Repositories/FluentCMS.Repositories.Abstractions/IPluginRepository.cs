namespace FluentCMS.Repositories.Abstractions;

public interface IPluginRepository : IAuditableEntityRepository<Plugin>
{
    Task<IEnumerable<Plugin>> GetByColumnId(Guid columnId, CancellationToken cancellationToken = default);
}
