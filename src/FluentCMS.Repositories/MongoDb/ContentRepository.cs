using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class ContentRepository<TContent>(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    IContentRepository<TContent>
    where TContent : Content, new()
{

    public async Task<TContent?> Create(TContent content, CancellationToken cancellationToken = default)
    {
        // setting base properties
        content.Id = Guid.NewGuid();
        content.CreatedAt = DateTime.UtcNow;
        content.LastUpdatedAt = DateTime.UtcNow;
        content.CreatedBy = applicationContext.Current.UserName;
        content.LastUpdatedBy = applicationContext.Current.UserName;

        var dict = content.ToDictionary();

        MakeMongoDBId(dict);

        var collection = GetCollection(content.Type);

        await collection.InsertOneAsync(dict, cancellationToken: cancellationToken);

        return await GetById(content.SiteId, content.Type, content.Id, cancellationToken);
    }

    public async Task<TContent?> GetById(Guid siteId, string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection(contentType);

        var filter = GetIdFilter(id);

        var inserted = await collection.FindAsync(filter, cancellationToken: cancellationToken);

        var dict = await inserted.FirstAsync(cancellationToken);

        ReverseMongoDBId(dict);

        return dict.ToContent<TContent>();
    }

    public async Task<TContent?> Update(TContent content, CancellationToken cancellationToken = default)
    {
        // setting base properties
        content.LastUpdatedAt = DateTime.UtcNow;
        content.LastUpdatedBy = applicationContext.Current.UserName;

        var existing = await GetById(content.SiteId, content.Type, content.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentNotFound);

        if (existing.Type != content.Type)
            throw new AppException(ExceptionCodes.ContentTypeMismatch);

        if (existing.SiteId != content.SiteId)
            throw new AppException(ExceptionCodes.ContentSiteIdMismatch);

        var dict = content.ToDictionary();

        MakeMongoDBId(dict);

        var collection = GetCollection(content.Type);

        var filter = GetIdFilter(content.Id);

        var updatedDict = await collection.FindOneAndReplaceAsync(filter, dict, null, cancellationToken);

        ReverseMongoDBId(updatedDict);

        return updatedDict.ToContent<TContent>();
    }

    public async Task<TContent?> Delete(Guid siteId, string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection(contentType);

        var filter = GetSiteIdFilter(siteId, id);

        var options = new FindOneAndDeleteOptions<Dictionary<string, object?>>();

        var deleted = await collection.FindOneAndDeleteAsync(filter, options, cancellationToken);

        if (deleted == null)
            return default;

        ReverseMongoDBId(deleted);
        return deleted.ToContent<TContent>();
    }

    public async Task<IEnumerable<TContent>> GetAll(Guid siteId, string contentType, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection(contentType);
        var filter = Builders<Dictionary<string, object?>>.Filter.Empty;
        var dictionaries = await collection.FindAsync(filter, cancellationToken: cancellationToken);
        return await Task.FromResult(dictionaries.ToEnumerable(cancellationToken: cancellationToken).Select(dict =>
        {
            ReverseMongoDBId(dict);
            return dict.ToContent<TContent>();
        }));
    }

    #region Private Methods

    protected IMongoCollection<Dictionary<string, object?>> GetCollection(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentNullException(nameof(contentType));

        return mongoDbContext.Database.GetCollection<Dictionary<string, object?>>(contentType);
    }

    protected static void ReverseMongoDBId(Dictionary<string, object?> dict)
    {
        if (dict.TryGetValue("_id", out object? value))
        {
            dict["Id"] = value;
            dict.Remove("_id");
        }
    }

    protected static void MakeMongoDBId(Dictionary<string, object?> dict)
    {
        if (dict.TryGetValue("Id", out object? value))
        {
            dict["_id"] = value;
            dict.Remove("Id");
        }
    }

    protected static FilterDefinition<Dictionary<string, object?>> GetSiteIdFilter(Guid siteId, Guid contentId)
    {
        var filter = GetIdFilter(contentId);
        filter &= GetSiteIdFilter(siteId);
        return filter;
    }

    protected static FilterDefinition<Dictionary<string, object?>> GetSiteIdFilter(Guid siteId)
    {
        var builder = Builders<Dictionary<string, object?>>.Filter;
        var filter = builder.Eq("SiteId", siteId);
        return filter;
    }

    protected static FilterDefinition<Dictionary<string, object?>> GetIdFilter(Guid id)
    {
        var builder = Builders<Dictionary<string, object?>>.Filter;
        var filter = builder.Eq("_id", id);
        return filter;
    }

    #endregion
}
