using FluentCMS.Entities;
using MongoDB.Bson;
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
        var bsonCollection = mongoDbContext.Database.GetCollection<BsonDocument>(contentType);
        var bsonFilter = Builders<BsonDocument>.Filter.Empty;
        var bsonDocs = await bsonCollection.FindAsync(bsonFilter, null, cancellationToken);
        var bsonDicts = await bsonDocs.ToListAsync(cancellationToken);

        var pluginContents = new List<PluginContent>();
        foreach (var doc in bsonDicts)
        {
            var dict = doc.ToDictionary();
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
