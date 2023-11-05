using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDb;

public interface IMongoDBContext
{
    IMongoDatabase Database { get; }
}
