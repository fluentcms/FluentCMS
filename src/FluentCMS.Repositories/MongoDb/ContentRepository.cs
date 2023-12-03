using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Reflection;

namespace FluentCMS.Repositories.MongoDb;

public class ContentRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    GenericRepository<Content>(mongoDbContext, applicationContext), IContentRepository
{

    private static BsonDocument ConvertToBsonDocument(Dictionary<string, object?> dictionary)
    {
        var document = new BsonDocument();

        foreach (var item in dictionary)
        {
            var key = item.Key;
            var value = item.Value;
            if (item.Key.Equals("id", StringComparison.CurrentCultureIgnoreCase))
            {
                key = "_id";
                value = Guid.NewGuid();
            }
            document[key] = ConvertToBsonValue(value);
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

    public override async Task<Content?> Create(Content entity, CancellationToken cancellationToken = default)
    {
        var dict = ConvertContentToDictionary(entity);
        var document = ConvertToBsonDocument(dict);
        var collection = MongoDatabase.GetCollection<BsonDocument>(entity.Type);

        try
        {
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            
            throw;
        }
        
        return entity;

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

}
