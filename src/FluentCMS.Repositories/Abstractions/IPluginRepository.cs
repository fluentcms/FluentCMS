using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IPluginRepository : ISiteAssociatedRepository<Plugin>
{
    Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default);
}
