using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDb;

public class MongoDbContext : IMongoDBContext
{
    public MongoDbContext(MongoDbOptions<MongoDbContext> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ConnectionString);

        var mongoUrl = new MongoUrl(options.ConnectionString);
        var client = new MongoClient(mongoUrl);

        Database = client.GetDatabase(mongoUrl.DatabaseName);
    }

    public IMongoDatabase Database { get; private set; }
}

