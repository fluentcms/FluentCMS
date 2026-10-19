using FluentCMS.Infrastructure.Persistence;
using Npgsql;

namespace FluentCMS.IntegrationTests;

// An empty database on either provider. On PostgreSQL it is created next to the configured one, which other tests expect to stay empty.
internal sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase? _sqlite;
    private readonly NpgsqlConnectionStringBuilder? _server;
    private readonly string? _postgresName;

    // Media bytes go to a directory of the database's own, so no test writes into the build output.
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"fluentcms-media-{Guid.NewGuid():N}");

    private TemporaryDatabase(Dictionary<string, string?> configuration, TemporarySqliteDatabase? sqlite, NpgsqlConnectionStringBuilder? server, string? postgresName)
    {
        Configuration = new Dictionary<string, string?>(configuration) { ["Media:LocalDisk:Root"] = _mediaRoot };
        _sqlite = sqlite;
        _server = server;
        _postgresName = postgresName;
    }

    public Dictionary<string, string?> Configuration { get; }

    public static async Task<TemporaryDatabase> CreateAsync(string provider)
    {
        if (provider != DatabaseProviders.PostgreSql)
        {
            var sqlite = new TemporarySqliteDatabase();
            return new TemporaryDatabase(sqlite.Configuration, sqlite, null, null);
        }

        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(PostgresFactAttribute.VariableName)!);
        var name = $"fluentcms_test_{Guid.NewGuid():N}";
        await ExecuteAsync(server, $"CREATE DATABASE \"{name}\"");

        var database = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = name };
        return new TemporaryDatabase(new() { ["Database:ConnectionString"] = database.ConnectionString }, null, server, name);
    }

    public async ValueTask DisposeAsync()
    {
        _sqlite?.Dispose();

        if (Directory.Exists(_mediaRoot))
        {
            Directory.Delete(_mediaRoot, recursive: true);
        }

        if (_server is not null)
        {
            await ExecuteAsync(_server, $"DROP DATABASE IF EXISTS \"{_postgresName}\" WITH (FORCE)");
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnectionStringBuilder server, string statement)
    {
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync();
    }
}
