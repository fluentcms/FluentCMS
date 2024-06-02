namespace FluentCMS.Repositories.MongoDB;

public interface IMongoDBContext
{
    IMongoDatabase Database { get; }
}

public class MongoDBContext : IMongoDBContext
{
    public MongoDBContext(MongoDBOptions<MongoDBContext> options)
    {
        // Validate input arguments to ensure they are not null.
        ArgumentNullException.ThrowIfNull(options, nameof(options));
        ArgumentNullException.ThrowIfNull(options.ConnectionString, nameof(options.ConnectionString));

        // Create a MongoUrl object based on the provided connection string.
        var mongoUrl = new MongoUrl(options.ConnectionString);

        // Create a MongoClient using the MongoUrl.
        var client = new MongoClient(mongoUrl);

        // Initialize the Database property using the database name from the MongoUrl.
        Database = client.GetDatabase(mongoUrl.DatabaseName);
    }

    public IMongoDatabase Database { get; private set; }
}
