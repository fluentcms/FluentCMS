using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class PluginContentRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    ContentRepository<PluginContent>(mongoDbContext, applicationContext),
    IPluginContentRepository
{
    public async Task<IEnumerable<PluginContent>> GetByPluginId(Guid siteId, string contentType, Guid pluginId, CancellationToken cancellationToken = default)
    {
        var filter = GetSiteIdFilter(siteId) & GetPluginIdFilter(pluginId);

        var dictionaries = await GetCollection(contentType).FindAsync(filter, null, cancellationToken);

        var pluginContents = new List<PluginContent>();
        foreach (var dict in await dictionaries.ToListAsync(cancellationToken))
        {
            ReverseMongoDBId(dict);
            pluginContents.Add(dict.ToContent<PluginContent>());
        }
        return pluginContents;
    }

    protected static FilterDefinition<Dictionary<string, object?>> GetPluginIdFilter(Guid pluginId)
    {
        var builder = Builders<Dictionary<string, object?>>.Filter;
        var filter = builder.Eq("PluginId", pluginId);
        return filter;
    }
}
