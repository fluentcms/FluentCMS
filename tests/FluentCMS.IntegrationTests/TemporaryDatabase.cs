using System.Text;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    // Whether any value stored anywhere holds the text: the bytes of the SQLite file, or every row of every table on PostgreSQL.
    public async Task<bool> StoresTextAsync(IServiceProvider services, string text)
    {
        if (_sqlite is not null)
        {
            SqliteConnection.ClearAllPools();
            var path = new SqliteConnectionStringBuilder(Configuration["Database:ConnectionString"]).DataSource;
            var bytes = await File.ReadAllBytesAsync(path);
            return bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;
        }

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var table in tables)
        {
            await using var search = connection.CreateCommand();
            search.CommandText = $"SELECT EXISTS (SELECT 1 FROM \"{table}\" AS stored WHERE strpos(stored::text, @text) > 0)";
            var parameter = search.CreateParameter();
            parameter.ParameterName = "text";
            parameter.Value = text;
            search.Parameters.Add(parameter);

            if ((bool)(await search.ExecuteScalarAsync())!)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task ExecuteAsync(NpgsqlConnectionStringBuilder server, string statement)
    {
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync();
    }
}
