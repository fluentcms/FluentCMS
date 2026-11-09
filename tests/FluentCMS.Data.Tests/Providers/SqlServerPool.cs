using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text;
using FluentCMS.Core.Storage;
using FluentCMS.Data.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Data.Tests.Providers;

// SQL Server takes seconds to create a database once tests create them side by side, and about a second to apply the
// system migrations to one, so the tests reuse their databases. A database a test is done with is renamed, so that work the
// test left running fails as it would on a dropped database instead of reaching the next test's. When what is left after
// dropping the test's own tables is the schema of the migrated template, the template's rows are put back, and the database
// serves the next test that applies the system migrations, which then find nothing to do. Any other database is emptied.
internal static class SqlServerPool
{
    private const string Template = "fluentcms_template";
    private const string TemplateTables = $"SELECT name FROM [{Template}].sys.tables WHERE is_ms_shipped = 0";

    private const int NoSuchDatabase = 911;
    private const int DatabaseInUse = 3702;
    private const int NoExclusiveLock = 5030;

    private static readonly ConcurrentBag<string> Migrated = [];
    private static readonly ConcurrentBag<string> Emptied = [];

    private static string _templateSchema = "";
    private static string _reset = "";

    public static void Start(string admin)
    {
        Run(admin, null, $"CREATE DATABASE [{Template}]");
        var provider = new SqlServerDataProvider(InTemplate(admin));
        var options = new DbContextOptionsBuilder<SystemDbContext>();
        provider.ConfigureSystemMigrations(options);
        options.UseSystemModel(provider.Conventions);
        using (var context = new SystemDbContext(options.Options))
        {
            context.Database.Migrate();
        }

        _templateSchema = SchemaOf(admin, Template);
        _reset = ResetScript(admin);
    }

    // A test that applies the system migrations first takes a migrated database, or else an empty one it migrates itself.
    public static string Take(string admin, bool forSystemTables)
    {
        if ((forSystemTables && Migrated.TryTake(out var name)) || Emptied.TryTake(out name))
        {
            return name;
        }

        while (!forSystemTables && Migrated.TryTake(out name))
        {
            if (TryEmpty(admin, name))
            {
                return name;
            }

            Drop(admin, name);
        }

        name = $"t_{Guid.NewGuid():N}";
        Run(admin, null, $"CREATE DATABASE [{name}]");
        return name;
    }

    public static void Release(string admin, string name)
    {
        var renamed = $"t_{Guid.NewGuid():N}";
        if (!TryRename(admin, name, renamed))
        {
            Drop(admin, name);
            return;
        }

        if (TryReset(admin, renamed))
        {
            Migrated.Add(renamed);
        }
        else if (TryEmpty(admin, renamed))
        {
            Emptied.Add(renamed);
        }
        else
        {
            Drop(admin, renamed);
        }
    }

    // SET SINGLE_USER WITH ROLLBACK IMMEDIATE ends a database's sessions too, but takes about 3 s whenever there is one.
    // Work the test left running can connect again between the kill and the rename, which then retries.
    private static bool TryRename(string admin, string name, string renamed)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Run(admin, null, $"""
                    DECLARE @kill nvarchar(max) = N'';
                    SELECT @kill += N'BEGIN TRY KILL ' + CAST(session_id AS nvarchar(10)) + N'; END TRY BEGIN CATCH END CATCH;'
                    FROM sys.dm_exec_sessions WHERE database_id = DB_ID('{name}') AND is_user_process = 1;
                    EXEC sp_executesql @kill;
                    ALTER DATABASE [{name}] MODIFY NAME = [{renamed}];
                    """);
                return true;
            }
            catch (SqlException exception) when (exception.Number is NoExclusiveLock or DatabaseInUse)
            {
                // A killed session can still be rolling back.
                Thread.Sleep(50 * attempt);
            }
            catch (SqlException exception) when (exception.Number == NoSuchDatabase)
            {
                return false;
            }
        }

        return false;
    }

    private static void Drop(string admin, string name) =>
        Run(admin, null, $"IF DB_ID('{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END");

    private static bool TryReset(string admin, string name)
    {
        try
        {
            Run(admin, name, DropScript(keepTemplateTables: true));
            if (SchemaOf(admin, name) != _templateSchema)
            {
                return false;
            }

            Run(admin, name, _reset);
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private static bool TryEmpty(string admin, string name)
    {
        try
        {
            return (int)Run(admin, name, $"""
                {DropScript(keepTemplateTables: false)}
                SELECT (SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped = 0)
                    + (SELECT COUNT(*) FROM sys.types WHERE is_user_defined = 1)
                    + (SELECT COUNT(*) FROM sys.schemas WHERE schema_id BETWEEN 5 AND 16383);
                """, scalar: true)! == 0;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    // Drops the foreign keys, views, tables and sequences a test made, and the template's tables too unless they are kept.
    private static string DropScript(bool keepTemplateTables)
    {
        var foreignKeys = keepTemplateTables ? $"WHERE OBJECT_NAME(parent_object_id) NOT IN ({TemplateTables})" : "";
        var tables = keepTemplateTables ? $"AND name NOT IN ({TemplateTables})" : "";
        return $"""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @drop nvarchar(max) = N'';
            SELECT @drop += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' FROM sys.foreign_keys {foreignKeys};
            SELECT @drop += N'DROP VIEW ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';' FROM sys.views WHERE is_ms_shipped = 0;
            SELECT @drop += N'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';' FROM sys.tables WHERE is_ms_shipped = 0 {tables};
            SELECT @drop += N'DROP SEQUENCE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';' FROM sys.sequences;
            EXEC sp_executesql @drop;
            COMMIT;
            """;
    }

    // Empties every table of the template, children first, and copies its rows back, parents first. A table nothing
    // references is truncated, which also starts its identity again; a referenced one is deleted from and reseeded.
    private static string ResetScript(string admin)
    {
        var tables = new List<(string Name, bool Referenced, long? Reseed, string Columns)>();
        var references = new List<(string Child, string Parent)>();
        var seeded = new HashSet<string>();
        using (var connection = new SqlConnection(InTemplate(admin)))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT t.name,
                    CAST(IIF(EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.referenced_object_id = t.object_id), 1, 0) AS bit),
                    (SELECT CAST(seed_value AS bigint) - CAST(increment_value AS bigint) FROM sys.identity_columns i WHERE i.object_id = t.object_id),
                    (SELECT STRING_AGG(CAST(QUOTENAME(c.name) AS nvarchar(max)), N', ') WITHIN GROUP (ORDER BY c.column_id)
                     FROM sys.columns c WHERE c.object_id = t.object_id AND c.is_computed = 0 AND c.system_type_id <> 189)
                FROM sys.tables t WHERE t.is_ms_shipped = 0;
                SELECT OBJECT_NAME(parent_object_id), OBJECT_NAME(referenced_object_id) FROM sys.foreign_keys WHERE parent_object_id <> referenced_object_id;
                """;
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    tables.Add((reader.GetString(0), reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.GetString(3)));
                }

                reader.NextResult();
                while (reader.Read())
                {
                    references.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            foreach (var table in tables)
            {
#pragma warning disable CA2100 // Names of the template's tables.
                command.CommandText = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM [{table.Name}]) THEN 1 ELSE 0 END";
#pragma warning restore CA2100
                if ((int)command.ExecuteScalar()! == 1)
                {
                    seeded.Add(table.Name);
                }
            }
        }

        var childrenFirst = new List<string>();
        var left = tables.Select(table => table.Name).ToHashSet();
        while (left.Count > 0)
        {
            var next = left.FirstOrDefault(table => !references.Any(reference => reference.Parent == table && left.Contains(reference.Child)))
                ?? throw new InvalidOperationException($"The system tables {string.Join(", ", left)} reference each other in a cycle, which the reset of a test database cannot order.");
            childrenFirst.Add(next);
            left.Remove(next);
        }

        var byName = tables.ToDictionary(table => table.Name);
        var script = new StringBuilder("SET XACT_ABORT ON; BEGIN TRANSACTION;");
        foreach (var table in childrenFirst.Select(name => byName[name]))
        {
            if (!table.Referenced)
            {
                script.Append(CultureInfo.InvariantCulture, $" TRUNCATE TABLE [{table.Name}];");
                continue;
            }

            script.Append(CultureInfo.InvariantCulture, $" DELETE FROM [{table.Name}];");
            if (table.Reseed is { } reseed)
            {
                // Only a table that ever had a row counts on from the reseed; a new one starts at the seed itself.
                script.Append(CultureInfo.InvariantCulture, $" IF EXISTS (SELECT 1 FROM sys.identity_columns WHERE object_id = OBJECT_ID(N'[{table.Name}]') AND last_value IS NOT NULL) DBCC CHECKIDENT (N'[{table.Name}]', RESEED, {reseed}) WITH NO_INFOMSGS;");
            }
        }

        foreach (var table in Enumerable.Reverse(childrenFirst).Where(seeded.Contains).Select(name => byName[name]))
        {
            var insert = $" INSERT INTO [{table.Name}] ({table.Columns}) SELECT {table.Columns} FROM [{Template}].[dbo].[{table.Name}];";
            script.Append(table.Reseed is not null ? $" SET IDENTITY_INSERT [{table.Name}] ON;{insert} SET IDENTITY_INSERT [{table.Name}] OFF;" : insert);
        }

        // A new database has no plans and no statistics yet. Plans and statistics left by earlier tests would plan this
        // test's queries for their rows instead.
        return script.Append("""
             COMMIT;
            DECLARE @statistics nvarchar(max) = N'';
            SELECT @statistics += N'UPDATE STATISTICS ' + QUOTENAME(OBJECT_SCHEMA_NAME(s.object_id)) + N'.' + QUOTENAME(OBJECT_NAME(s.object_id)) + N' (' + QUOTENAME(s.name) + N') WITH FULLSCAN;'
            FROM sys.stats s JOIN sys.tables t ON t.object_id = s.object_id CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) p
            WHERE t.is_ms_shipped = 0 AND (p.modification_counter > 0 OR p.rows <> (SELECT SUM(rows) FROM sys.partitions WHERE object_id = s.object_id AND index_id IN (0, 1)));
            EXEC sp_executesql @statistics;
            ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
            """).ToString();
    }

    // Every object, column, index, constraint, type and schema a test could have changed, without the names SQL Server
    // makes up for a constraint, which differ from database to database.
    private static string SchemaOf(string admin, string name) => (string)Run(admin, name, """
        SELECT CONVERT(nvarchar(64), HASHBYTES('SHA2_256', ISNULL(STRING_AGG(CAST(entry AS nvarchar(max)) COLLATE Latin1_General_BIN2, N'|') WITHIN GROUP (ORDER BY entry COLLATE Latin1_General_BIN2), N'')), 2) FROM (
            SELECT CONCAT(N'o:', type, N':', name) FROM sys.objects WHERE is_ms_shipped = 0 AND type NOT IN ('C', 'D', 'F', 'PK', 'UQ')
            UNION ALL SELECT CONCAT(N'c:', OBJECT_NAME(c.object_id), N':', c.column_id, N':', c.name, N':', c.user_type_id, N':', c.max_length, N':', c.precision, N':', c.scale, N':', c.is_nullable, N':', c.is_identity, N':', c.is_computed, N':', c.collation_name)
                FROM sys.columns c JOIN sys.objects o ON o.object_id = c.object_id WHERE o.is_ms_shipped = 0
            UNION ALL SELECT CONCAT(N'i:', OBJECT_NAME(i.object_id), N':', IIF(i.is_primary_key = 1, N'', i.name), N':', i.type, N':', i.is_unique, N':', i.filter_definition, N':', ic.key_ordinal, N':', ic.is_descending_key, N':', ic.is_included_column, N':', COL_NAME(ic.object_id, ic.column_id))
                FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id JOIN sys.objects o ON o.object_id = i.object_id WHERE o.is_ms_shipped = 0
            UNION ALL SELECT CONCAT(N'd:', OBJECT_NAME(parent_object_id), N':', COL_NAME(parent_object_id, parent_column_id), N':', IIF(is_system_named = 1, N'', name), N':', definition) FROM sys.default_constraints
            UNION ALL SELECT CONCAT(N'k:', OBJECT_NAME(parent_object_id), N':', IIF(is_system_named = 1, N'', name), N':', definition) FROM sys.check_constraints
            UNION ALL SELECT CONCAT(N'f:', OBJECT_NAME(parent_object_id), N':', IIF(is_system_named = 1, N'', name), N':', OBJECT_NAME(referenced_object_id), N':', delete_referential_action, N':', is_disabled, N':', is_not_trusted) FROM sys.foreign_keys
            UNION ALL SELECT CONCAT(N't:', name) FROM sys.types WHERE is_user_defined = 1
            UNION ALL SELECT CONCAT(N's:', name) FROM sys.schemas WHERE schema_id BETWEEN 5 AND 16383
        ) entries(entry);
        """, scalar: true)!;

    private static string InTemplate(string admin) => new SqlConnectionStringBuilder(admin) { InitialCatalog = Template, Pooling = false }.ToString();

    // Runs on the pooled connection to master, in the database when one is named, so that no session stays in it.
    private static object? Run(string admin, string? database, string sql, bool scalar = false)
    {
        using var connection = new SqlConnection(admin);
        connection.Open();
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Names of the tests' own databases.
        command.CommandText = database is null ? sql : $"EXEC [{database}].sys.sp_executesql @sql";
#pragma warning restore CA2100
        if (database is not null)
        {
            command.Parameters.Add(new SqlParameter("@sql", SqlDbType.NVarChar, -1) { Value = sql });
        }

        return scalar ? command.ExecuteScalar() : command.ExecuteNonQuery();
    }
}
