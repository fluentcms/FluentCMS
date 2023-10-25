using LiteDB.Async;

namespace FluentCMS.Repository.LiteDb;

// TODO: this class could be internal. its public for now because the tests
public class LiteDbContext
{
    private readonly LiteDatabaseAsync _db = default!;
    public LiteDatabaseAsync Database => _db;

    public LiteDbContext(LiteDbOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.FilePath) == false)
        {
            var connectionString = $"Filename={options.FilePath};Connection=shared";
            if (string.IsNullOrWhiteSpace(options.Password) == false)
                connectionString += ";Password=" + options.Password;

            _db = new LiteDatabaseAsync(connectionString);
        }
        else
        {
            _db = new LiteDatabaseAsync(new MemoryStream());
        }
    }
}
