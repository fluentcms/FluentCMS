using FluentCMS.Database.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.RegularExpressions;

namespace FluentCMS.Database.SqlServer;

internal sealed class SqlServerDatabaseManager : IDatabaseManager
{
    private readonly string _connectionString;
    private readonly SqlConnectionStringBuilder _csb;
    private readonly string _databaseName;        // Initial Catalog / Database
    private readonly string _attachFileName;      // AttachDbFilename (optional)
    private readonly ILogger<SqlServerDatabaseManager> _logger;

    public SqlServerDatabaseManager(string connectionString, ILogger<SqlServerDatabaseManager> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _csb = new SqlConnectionStringBuilder(connectionString);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _databaseName = _csb.InitialCatalog ?? _csb["Initial Catalog"]?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_databaseName) && _csb.ContainsKey("Database"))
            _databaseName = _csb["Database"]?.ToString() ?? string.Empty;

        _attachFileName = _csb.AttachDBFilename ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_databaseName))
            throw new ArgumentException("Connection string must include an Initial Catalog/Database name.", nameof(connectionString));
    }

    // -------------------- Public API --------------------

    public async Task<bool> DatabaseExists(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking if database '{DatabaseName}' exists.", _databaseName);
        var masterCsb = CloneForMaster(_csb);
        await using var conn = new SqlConnection(masterCsb.ConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN DB_ID(@db) IS NULL THEN 0 ELSE 1 END";
        cmd.Parameters.Add(new SqlParameter("@db", SqlDbType.NVarChar, 128) { Value = _databaseName });

        var result = (int)await cmd.ExecuteScalarAsync(cancellationToken);
        _logger.LogInformation("Database '{DatabaseName}' existence check result: {Result}.", _databaseName, result == 1);
        return result == 1;
    }

    public async Task CreateDatabase(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating database '{DatabaseName}' if it does not exist.", _databaseName);
        if (await DatabaseExists(cancellationToken))
        {
            _logger.LogInformation("Database '{DatabaseName}' already exists. Skipping creation.", _databaseName);
            return;
        }

        var masterCsb = CloneForMaster(_csb);
        await using var conn = new SqlConnection(masterCsb.ConnectionString);
        await conn.OpenAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(_attachFileName))
        {
            _logger.LogInformation("Creating database '{DatabaseName}' with attached file '{AttachFileName}'.", _databaseName, _attachFileName);
            var fullPath = Path.GetFullPath(_attachFileName);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                _logger.LogInformation("Created directory '{Directory}' for database files.", dir);
            }

            var logicalName = SanitizeForLogicalName(_databaseName);
            var mdf = fullPath;
            var ldf = Path.ChangeExtension(fullPath, ".ldf");

            var sql = $@"
                        IF DB_ID(@db) IS NULL
                        BEGIN
                            DECLARE @mdf NVARCHAR(4000) = @mdfPath;
                            DECLARE @ldf NVARCHAR(4000) = @ldfPath;

                            EXEC('CREATE DATABASE [{Escape(_databaseName)}]
                                  ON (NAME = N''{logicalName}'', FILENAME = ''' + @mdf + '''), 
                                     (NAME = N''{logicalName}_log'', FILENAME = ''' + @ldf + ''')
                                  FOR ATTACH');

                            IF DB_ID(@db) IS NULL
                            BEGIN
                                EXEC('CREATE DATABASE [{Escape(_databaseName)}]
                                      ON PRIMARY (NAME = N''{logicalName}'', FILENAME = ''' + @mdf + ''')
                                      LOG ON (NAME = N''{logicalName}_log'', FILENAME = ''' + @ldf + ''')');
                            END
                        END";

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new SqlParameter("@db", SqlDbType.NVarChar, 128) { Value = _databaseName });
            cmd.Parameters.Add(new SqlParameter("@mdfPath", SqlDbType.NVarChar, 4000) { Value = mdf });
            cmd.Parameters.Add(new SqlParameter("@ldfPath", SqlDbType.NVarChar, 4000) { Value = ldf });
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            _logger.LogInformation("Creating database '{DatabaseName}' in default locations.", _databaseName);
            var sql = $"IF DB_ID(@db) IS NULL CREATE DATABASE [{Escape(_databaseName)}];";
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new SqlParameter("@db", SqlDbType.NVarChar, 128) { Value = _databaseName });
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await WaitForDatabaseOnline(conn, _databaseName, cancellationToken);
        _logger.LogInformation("Database '{DatabaseName}' created successfully.", _databaseName);
    }

    public async Task<bool> TablesExist(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking if tables exist in database '{DatabaseName}'.", _databaseName);
        var entries = NormalizeTables(tableNames);
        if (entries.Count == 0) return true;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        foreach (var (schema, name) in entries)
        {
            var exists = await TableExists(conn, schema, name, cancellationToken);
            _logger.LogInformation("Table '{Schema}.{Table}' existence check result: {Result}.", schema, name, exists);
            if (!exists) return false;
        }
        return true;
    }

    public async Task<bool> TablesEmpty(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking if tables are empty in database '{DatabaseName}'.", _databaseName);
        var entries = NormalizeTables(tableNames);
        if (entries.Count == 0) return true;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        foreach (var (schema, name) in entries)
        {
            if (!await TableExists(conn, schema, name, cancellationToken))
            {
                _logger.LogWarning("Table '{Schema}.{Table}' does not exist.", schema, name);
                return false;
            }

            var sql = $"IF EXISTS (SELECT TOP (1) 1 FROM [{Escape(schema)}].[{Escape(name)}]) SELECT 1 ELSE SELECT 0;";
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var hasRows = (int)await cmd.ExecuteScalarAsync(cancellationToken) == 1;
            _logger.LogInformation("Table '{Schema}.{Table}' empty check result: {Result}.", schema, name, !hasRows);
            if (hasRows) return false;
        }
        return true;
    }

    public async Task ExecuteRaw(string sql, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing raw SQL command on database '{DatabaseName}'.", _databaseName);
        if (string.IsNullOrWhiteSpace(sql)) return;

        var batches = SplitSqlBatches(sql);
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        foreach (var (batchText, count) in batches)
        {
            for (int i = 0; i < count; i++)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = batchText;
                cmd.CommandType = CommandType.Text;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    // -------------------- Helpers --------------------

    private static SqlConnectionStringBuilder CloneForMaster(SqlConnectionStringBuilder csb)
    {
        var clone = new SqlConnectionStringBuilder(csb.ConnectionString)
        {
            InitialCatalog = "master"
        };
        // If Database key is present, mirror the change:
        if (clone.ContainsKey("Database"))
            clone["Database"] = "master";
        return clone;
    }

    private static string SanitizeForLogicalName(string name)
    {
        // Logical file names: letters, digits, underscore are safe
        var sanitized = new string(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        return string.IsNullOrEmpty(sanitized) ? "db" : sanitized;
    }

    private static string Escape(string identifier) => identifier.Replace("]", "]]");

    private static async Task WaitForDatabaseOnline(SqlConnection masterConn, string dbName, CancellationToken ct)
    {
        var sql = @"
                    DECLARE @status INT;
                    SELECT @status = state FROM sys.databases WHERE name = @db;
                    SELECT ISNULL(@status, -1);";
        // state = 0 -> ONLINE
        for (int i = 0; i < 20; i++)
        {
            await using var cmd = masterConn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new SqlParameter("@db", SqlDbType.NVarChar, 128) { Value = dbName });
            var state = (int)await cmd.ExecuteScalarAsync(ct);
            if (state == 0) return;
            await Task.Delay(250, ct);
        }
    }

    private static async Task<bool> TableExists(SqlConnection conn, string schema, string name, CancellationToken ct)
    {
        const string sql = @"
                            SELECT 1
                            FROM sys.tables t
                            JOIN sys.schemas s ON s.schema_id = t.schema_id
                            WHERE s.name = @schema AND t.name = @name;";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });
        cmd.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = name });
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null;
    }

    private static List<(string schema, string name)> NormalizeTables(IEnumerable<string> tableNames)
    {
        var list = new List<(string schema, string name)>();
        if (tableNames == null) return list;

        foreach (var raw in tableNames)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var trimmed = raw.Trim();

            // Accept forms: "Table", "schema.Table", "[schema].[Table]"
            var schema = "dbo";
            var name = trimmed;

            if (trimmed.Contains(".", StringComparison.Ordinal))
            {
                var parts = trimmed.Split('.', 2);
                schema = UnwrapIdentifier(parts[0]);
                name = UnwrapIdentifier(parts[1]);
            }
            else
            {
                name = UnwrapIdentifier(trimmed);
            }

            if (!string.IsNullOrWhiteSpace(name))
                list.Add((schema, name));
        }

        // distinct, case-insensitive
        return list
            .Distinct(StringTupleComparer.OrdinalIgnoreCase)
            .ToList();

        static string UnwrapIdentifier(string s)
        {
            s = s.Trim();
            if (s.StartsWith("[") && s.EndsWith("]") && s.Length >= 2)
                s = s.Substring(1, s.Length - 2);
            return s;
        }
    }

    private static readonly Regex GoRegex =
        new Regex(@"^\s*GO\s*(\d+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Splits a SQL script into batches on lines containing only 'GO' (optionally with a repeat count).
    /// Returns tuples of (batchText, count).
    /// </summary>
    private static IEnumerable<(string batch, int count)> SplitSqlBatches(string sql)
    {
        var parts = new List<(string, int)>();
        int lastIndex = 0;

        foreach (Match m in GoRegex.Matches(sql))
        {
            var batchText = sql.Substring(lastIndex, m.Index - lastIndex).Trim();
            if (!string.IsNullOrWhiteSpace(batchText))
            {
                var count = 1;
                if (m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out var n) && n > 0)
                    count = n;

                parts.Add((batchText, count));
            }
            lastIndex = m.Index + m.Length;
        }

        var tail = sql[lastIndex..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            parts.Add((tail, 1));

        return parts;
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string a, string b)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new();
        public bool Equals((string a, string b) x, (string a, string b) y) =>
            string.Equals(x.a, y.a, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.b, y.b, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string a, string b) obj) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.a) * 397 ^
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.b);
    }
}
