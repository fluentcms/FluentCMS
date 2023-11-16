namespace FluentCMS.Repositories.MongoDB;

public class MongoDBOptions<TContext>(string connectionString) where TContext : IMongoDBContext
{
    public string ConnectionString { get; } = connectionString;
}
