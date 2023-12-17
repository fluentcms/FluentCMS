namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Represents configuration options for a MongoDB context.
/// This class encapsulates settings specific to MongoDB access, such as the connection string.
/// </summary>
/// <typeparam name="TContext">The type of the MongoDB context these options apply to.</typeparam>
public class MongoDBOptions<TContext> where TContext : IMongoDBContext
{
    /// <summary>
    /// Initializes a new instance of the MongoDBOptions class with the specified connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to the MongoDB server.</param>
    public MongoDBOptions(string connectionString)
    {
        // Guard clause to ensure the connection string is not null or empty
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        ConnectionString = connectionString;
    }

    /// <summary>
    /// Gets the MongoDB connection string.
    /// This connection string is used to connect to the MongoDB server and access the database.
    /// </summary>
    public string ConnectionString { get; }
}
