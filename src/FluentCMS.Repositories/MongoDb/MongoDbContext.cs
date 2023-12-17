using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Defines the contract for a MongoDB context, encapsulating access to a MongoDB database.
/// </summary>
public interface IMongoDBContext
{
    /// <summary>
    /// Gets the MongoDB database instance.
    /// </summary>
    IMongoDatabase Database { get; }
}

/// <summary>
/// Implementation of IMongoDBContext, providing access to a MongoDB database.
/// </summary>
public class MongoDBContext : IMongoDBContext
{
    /// <summary>
    /// Initializes a new instance of the MongoDBContext class using the specified MongoDB options.
    /// </summary>
    /// <param name="options">Configuration options for connecting to MongoDB.</param>
    /// <exception cref="ArgumentNullException">Thrown if options or options.ConnectionString is null.</exception>
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

    /// <summary>
    /// Gets the MongoDB database instance, encapsulating the connection to the database.
    /// </summary>
    public IMongoDatabase Database { get; private set; }
}
