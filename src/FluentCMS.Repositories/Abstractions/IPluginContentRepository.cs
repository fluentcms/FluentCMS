using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IPluginContentRepository : IContentRepository<PluginContent>
{
    Task<IEnumerable<PluginContent>> GetByPluginId(string contentType, Guid pluginId, CancellationToken cancellationToken = default);
}
