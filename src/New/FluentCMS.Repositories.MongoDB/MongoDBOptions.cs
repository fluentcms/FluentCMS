namespace FluentCMS.Repositories.MongoDB;

public class MongoDBOptions<TContext> where TContext : IMongoDBContext
{
    public MongoDBOptions(string connectionString)
    {
        // Guard clause to ensure the connection string is not null or empty
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
