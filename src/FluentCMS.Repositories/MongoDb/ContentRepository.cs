using FluentCMS.Entities;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Reflection;

namespace FluentCMS.Repositories.MongoDB;

public class ContentRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) : IContentRepository
{
    #region Private Fields

    private static BsonDocument ConvertToBsonDocument(Dictionary<string, object?> dictionary)
    {
        var document = new BsonDocument();

        foreach (var item in dictionary)
        {
            var key = item.Key;

            if (item.Key.Equals("id", StringComparison.CurrentCultureIgnoreCase))
                key = "_id";

            document[key] = ConvertToBsonValue(item.Value);
        }

        return document;
    }

    private static BsonValue ConvertToBsonValue(object? value)
    {
        // Handle null values explicitly
        if (value == null)
            return BsonNull.Value;

        // Handle nested dictionaries recursively
        if (value is Dictionary<string, object?> nestedDictionary)
            return ConvertToBsonDocument(nestedDictionary);

        // Handle other types
        return BsonValue.Create(value);
    }


    // we need to convert the bson document to a content object
    private static Content ConvertBsonDocumentToContent(BsonDocument document)
    {
        var content = new Content();
        foreach (var item in document)
        {
            var key = item.Name;
            var value = item.Value;
            if (item.Name.Equals("_id", StringComparison.CurrentCultureIgnoreCase))
            {
                key = "Id";
            }
            content[key] = ConvertBsonValueToContentValue(value);
        }

        return content;
    }

    // we need to convert the bson value to a content value
    private static object? ConvertBsonValueToContentValue(BsonValue value)
    {
        // Handle null values explicitly
        if (value == null)
            return null;

        // Handle nested dictionaries recursively
        if (value is BsonDocument nestedDocument)
            return ConvertBsonDocumentToContent(nestedDocument);

        // Handle other types
        return value.AsString;
    }

    private static Dictionary<string, object?> ConvertContentToDictionary(Content content)
    {
        var result = new Dictionary<string, object?>();

        // Using reflection to get properties of the Content class
        foreach (PropertyInfo prop in typeof(Content).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            result[prop.Name] = prop.GetValue(content);
        }

        // Adding the dictionary entries
        foreach (var kvp in content)
        {
            // This will overwrite any property with the same name as a dictionary key
            result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    #endregion

    private IMongoCollection<Dictionary<string, object?>> GetCollection(string contentType)
    {
        return mongoDbContext.Database.GetCollection<Dictionary<string, object?>>(contentType);
    }

    public async Task<Content?> Create(Content entity, CancellationToken cancellationToken = default)
    {
        // setting base properties
        entity.Id = Guid.NewGuid();
        entity.CreatedAt = DateTime.UtcNow;
        entity.LastUpdatedAt = DateTime.UtcNow;
        entity.CreatedBy = applicationContext.Current.UserName;
        entity.LastUpdatedBy = applicationContext.Current.UserName;

        var dict = ConvertContentToDictionary(entity);
        if (dict.TryGetValue("Id", out object? value))
        {
            dict["_id"] = value;
            dict.Remove("Id");
        }

        var collection = GetCollection(entity.Type);

        await collection.InsertOneAsync(dict, cancellationToken: cancellationToken);

        return await GetById(entity.Type, entity.Id, cancellationToken);
    }

    public async Task<Content?> GetById(string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection(contentType);
        var filter = Builders<Dictionary<string, object?>>.Filter.Eq("_id", id);
        var inserted = await collection.FindAsync(filter, cancellationToken: cancellationToken);
        var dict = await inserted.FirstAsync(cancellationToken);
        if (dict.TryGetValue("_id", out object? value))
        {
            dict["Id"] = value;
            dict.Remove("_id");
        }
        return Content.FromDictionary(dict);
    }

    public Task<Content?> Update(Content content, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Content?> Delete(string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<Content>> GetAll(string contentType, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
