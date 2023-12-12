using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IPluginContentService : IContentService<PluginContent>
{
    Task<IEnumerable<PluginContent>> GetByPluginId(Guid siteId, string contentType, Guid pluginId, CancellationToken cancellationToken = default);
}

public class PluginContentService(
    IPluginContentRepository contentRepository) :
    ContentService<PluginContent>(contentRepository),
    IPluginContentService
{

    public async Task<IEnumerable<PluginContent>> GetByPluginId(Guid siteId, string contentType, Guid pluginId, CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetByPluginId(siteId, contentType, pluginId, cancellationToken);
    }

    public override Task<PluginContent> Create(PluginContent content, CancellationToken cancellationToken = default)
    {
        return base.Create(content, cancellationToken);
    }

}
