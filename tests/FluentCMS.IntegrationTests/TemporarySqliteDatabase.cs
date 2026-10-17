using Microsoft.Data.Sqlite;

namespace FluentCMS.IntegrationTests;

internal sealed class TemporarySqliteDatabase : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fluentcms-test-{Guid.NewGuid():N}.db");

    public Dictionary<string, string?> Configuration => new()
    {
        ["Database:ConnectionString"] = $"Data Source={_path}",
    };

    public void Dispose()
    {
        // Pooled connections keep the file locked on Windows until the pool is cleared.
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }
}
