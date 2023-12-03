using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class ContentRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) : IContentRepository
{

    public async Task<Content?> Create(Content content, CancellationToken cancellationToken = default)
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

        return await GetById(content.Type, content.Id, cancellationToken);
    }

    public async Task<Content?> GetById(string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection(contentType);
        var filter = Builders<Dictionary<string, object?>>.Filter.Eq("_id", id);
        var inserted = await collection.FindAsync(filter, cancellationToken: cancellationToken);
        var dict = await inserted.FirstAsync(cancellationToken);
        ReverseMongoDBId(dict);
        return dict.ToContent();
    }

    public async Task<Content?> Update(Content content, CancellationToken cancellationToken = default)
    {
        // setting base properties
        content.LastUpdatedAt = DateTime.UtcNow;
        content.LastUpdatedBy = applicationContext.Current.UserName;

        var existing = await GetById(content.Type, content.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentNotFound);

        if (existing.Type != content.Type)
            throw new AppException(ExceptionCodes.ContentTypeMismatch);

        if (existing.SiteId != content.SiteId)
            throw new AppException(ExceptionCodes.ContentSiteIdMismatch);

        var dict = content.ToDictionary();
        MakeMongoDBId(dict);

        var collection = GetCollection(content.Type);

        var filter = Builders<Dictionary<string, object?>>.Filter.Eq("_id", content.Id);

        var updatedDict = await collection.FindOneAndReplaceAsync(filter, dict, null, cancellationToken);

        ReverseMongoDBId(updatedDict);

        return updatedDict.ToContent();
    }

    public async Task<Content?> Delete(string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection(contentType);
        var filter = Builders<Dictionary<string, object?>>.Filter.Eq("_id", id);
        var options = new FindOneAndDeleteOptions<Dictionary<string, object?>>();
        var deleted = await collection.FindOneAndDeleteAsync(filter, options, cancellationToken);

        if (deleted == null)
            return default;

        ReverseMongoDBId(deleted);
        return deleted.ToContent();
    }

    public Task<IEnumerable<Content>> GetAll(string contentType, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection(contentType);
        var filter = Builders<Dictionary<string, object?>>.Filter.Empty;
        var dictionaries = collection.FindAsync(filter, cancellationToken: cancellationToken);
        return Task.FromResult(dictionaries.Result.ToEnumerable(cancellationToken: cancellationToken).Select(dict =>
        {
            ReverseMongoDBId(dict);
            return dict.ToContent();
        }));
    }

    #region Private Methods

    private IMongoCollection<Dictionary<string, object?>> GetCollection(string contentType)
    {
        return mongoDbContext.Database.GetCollection<Dictionary<string, object?>>(contentType);
    }

    private static void ReverseMongoDBId(Dictionary<string, object?> dict)
    {
        if (dict.TryGetValue("_id", out object? value))
        {
            dict["Id"] = value;
            dict.Remove("_id");
        }
    }

    private static void MakeMongoDBId(Dictionary<string, object?> dict)
    {
        if (dict.TryGetValue("Id", out object? value))
        {
            dict["_id"] = value;
            dict.Remove("Id");
        }
    }

    #endregion
}
