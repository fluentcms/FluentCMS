namespace FluentCMS.Repositories.LiteDb;

public interface ILiteDBContext
{
    ILiteDatabaseAsync Database { get; }
}

public class LiteDBContext : ILiteDBContext
{
    public LiteDBContext(LiteDBOptions<LiteDBContext> options)
    {
        // Validate input arguments to ensure they are not null.
        ArgumentNullException.ThrowIfNull(options, nameof(options));
        ArgumentNullException.ThrowIfNull(options.ConnectionString, nameof(options.ConnectionString));

        Database = new LiteDatabaseAsync(options.ConnectionString);
    }

    public ILiteDatabaseAsync Database { get; }
}
