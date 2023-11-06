using LiteDB.Async;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbContext
{
    private readonly LiteDatabaseAsync _db = default!;
    public LiteDatabaseAsync Database => _db;

    public LiteDbContext(LiteDbOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.ConnectionString) == false)
        {
            _db = new LiteDatabaseAsync(options.ConnectionString);
        }
        else
        {
            _db = new LiteDatabaseAsync(new MemoryStream());
        }
    }
}
