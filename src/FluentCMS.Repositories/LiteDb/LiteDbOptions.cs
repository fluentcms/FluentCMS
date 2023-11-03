using LiteDB.Async;
using Microsoft.Extensions.Options;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbContext
{
    private readonly LiteDatabaseAsync _db = default!;
    public LiteDatabaseAsync Database => _db;

    public LiteDbContext(IOptions<LiteDbOptions> options)
    {
        if (options.Value is null)
            throw new ArgumentNullException(nameof(options));

        var liteDbConfig = options.Value;


        if (string.IsNullOrWhiteSpace(liteDbConfig.FilePath) == false)
        {
            var connectionString = $"Filename={liteDbConfig.FilePath};Connection=shared";

            if (string.IsNullOrWhiteSpace(liteDbConfig.Password) == false)
                connectionString += ";Password=" + liteDbConfig.Password;

            _db = new LiteDatabaseAsync(connectionString);
        }
        else
        {
            _db = new LiteDatabaseAsync(new MemoryStream());
        }
    }
}
