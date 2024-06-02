namespace FluentCMS.Repositories.LiteDb;

public class LiteDBOptions<TContext> where TContext : ILiteDBContext
{
    public LiteDBOptions(string connectionString)
    {
        // Guard clause to ensure the connection string is not null or empty
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
