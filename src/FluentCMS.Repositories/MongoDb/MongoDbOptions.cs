namespace FluentCMS.Repositories.MongoDb;

public class MongoDbOptions<TContext>(string connectionString) where TContext : IMongoDBContext
{
    public string ConnectionString { get; } = connectionString;
}