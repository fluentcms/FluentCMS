using FluentCMS.Database.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Data;

namespace FluentCMS.Database.Sqlite;

internal sealed class SqliteDatabaseManager : IDatabaseManager
{
    private readonly string _connectionString;
    private readonly string _dataSource;           // parsed Data Source
    private readonly bool _isMemory;               // :memory: / Mode=Memory
    private readonly ILogger<SqliteDatabaseManager> _logger;

    public SqliteDatabaseManager(string connectionString, ILogger<SqliteDatabaseManager> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var csb = new SqliteConnectionStringBuilder(connectionString);

        _dataSource = csb.DataSource ?? string.Empty;
        _isMemory = string.Equals(_dataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(csb.Mode.ToString(), "Memory", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation("SqliteDatabaseManager initialized with DataSource: {DataSource}, IsMemory: {IsMemory}", _dataSource, _isMemory);
    }

    public async Task<bool> DatabaseExists(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking if database exists...");
        if (_isMemory)
        {
            try
            {
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                _logger.LogInformation("In-memory database exists.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check in-memory database existence.");
                return false;
            }
        }

        if (IsPlainFilePath(_dataSource))
        {
            var exists = File.Exists(_dataSource);
            _logger.LogInformation("File database existence check: {Exists}", exists);
            return exists;
        }

        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            _logger.LogInformation("Database exists.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check database existence.");
            return false;
        }
    }

    public async Task CreateDatabase(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating database...");
        if (_isMemory)
        {
            await using var memConn = new SqliteConnection(_connectionString);
            await memConn.OpenAsync(cancellationToken);
            _logger.LogInformation("In-memory database created.");
            return;
        }

        if (IsPlainFilePath(_dataSource))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_dataSource));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                _logger.LogInformation("Created directory for database: {Directory}", dir);
            }
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        _ = await cmd.ExecuteScalarAsync(cancellationToken);
        _logger.LogInformation("Database created successfully.");
    }

    public async Task<bool> TablesExist(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking if tables exist...");
        var names = NormalizeList(tableNames);
        if (names.Count == 0)
        {
            _logger.LogInformation("No table names provided. Returning true.");
            return true;
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var placeholders = string.Join(",", names.Select((_, i) => $"@p{i}"));
        var sql = @$"
                    SELECT name 
                    FROM sqlite_master 
                    WHERE type = 'table' AND name IN ({placeholders});
                    ";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        for (int i = 0; i < names.Count; i++)
            cmd.Parameters.AddWithValue($"@p{i}", names[i]);

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            found.Add(reader.GetString(0));

        var result = names.All(found.Contains);
        _logger.LogInformation("Tables existence check result: {Result}", result);
        return result;
    }

    public async Task<bool> TablesEmpty(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking if tables are empty...");
        var names = NormalizeList(tableNames);
        if (names.Count == 0)
        {
            _logger.LogInformation("No table names provided. Returning true.");
            return true;
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        foreach (var name in names)
        {
            if (!await TableExists(conn, name, cancellationToken))
            {
                _logger.LogInformation("Table {TableName} does not exist.", name);
                return false;
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM \"{name}\" LIMIT 1;";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (result != null)
            {
                _logger.LogInformation("Table {TableName} is not empty.", name);
                return false;
            }
        }

        _logger.LogInformation("All tables are empty.");
        return true;
    }

    public async Task ExecuteRaw(string sql, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing raw SQL...");
        if (string.IsNullOrWhiteSpace(sql))
        {
            _logger.LogWarning("Empty or null SQL provided. Skipping execution.");
            return;
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        var affectedRows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Raw SQL executed. Rows affected: {AffectedRows}", affectedRows);
    }

    // ---------- helpers ----------

    private static bool IsPlainFilePath(string dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource)) return false;
        if (string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase)) return false;
        if (dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static List<string> NormalizeList(IEnumerable<string> names) =>
        names?.Where(s => !string.IsNullOrWhiteSpace(s))
              .Select(s => s.Trim())
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .ToList() ?? [];

    private static async Task<bool> TableExists(SqliteConnection conn, string tableName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n;";
        cmd.Parameters.AddWithValue("@n", tableName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null;
    }
}
