using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public interface IMongoDBContext
{
    IMongoDatabase Database { get; }
}

public class MongoDBContext : IMongoDBContext
{
    public MongoDBContext(MongoDBOptions<MongoDBContext> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ConnectionString);

        var mongoUrl = new MongoUrl(options.ConnectionString);
        var client = new MongoClient(mongoUrl);

        Database = client.GetDatabase(mongoUrl.DatabaseName);
    }

    public IMongoDatabase Database { get; private set; }
}

