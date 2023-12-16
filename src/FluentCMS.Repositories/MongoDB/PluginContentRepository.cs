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
        var bsonCollection = GetBsonCollection(contentType);
        var builder = Builders<BsonDocument>.Filter;
        var filter = builder.Eq("PluginId", pluginId);
        filter &= builder.Eq("SiteId", siteId);
        var bsonDocs = await bsonCollection.FindAsync(filter, null, cancellationToken);
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

    public override async Task<PluginContent?> Update(PluginContent content, CancellationToken cancellationToken = default)
    {
        var existing = await GetById(content.SiteId, content.Type, content.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentNotFound);

        if (existing.PluginId != content.PluginId)
            throw new AppException(ExceptionCodes.ContentPluginIdMismatch);

        return await base.Update(content, cancellationToken);
    }

    protected static FilterDefinition<Dictionary<string, object?>> GetPluginIdFilter(Guid pluginId)
    {
        var builder = Builders<Dictionary<string, object?>>.Filter;
        var filter = builder.Eq("PluginId", pluginId);
        return filter;
    }

    protected override IMongoCollection<Dictionary<string, object?>> GetCollection(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentNullException(nameof(contentType));

        var collectionName = $"PluginContent_{contentType.ToLower()}";

        return MongoDbContext.Database.GetCollection<Dictionary<string, object?>>(collectionName);
    }

    protected IMongoCollection<BsonDocument> GetBsonCollection(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentNullException(nameof(contentType));

        var collectionName = $"PluginContent_{contentType.ToLower()}";

        return MongoDbContext.Database.GetCollection<BsonDocument>(collectionName);
    }

}
