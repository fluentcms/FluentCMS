using System.Text.RegularExpressions;
using FluentCMS.Data.Tests.Providers;

namespace FluentCMS.Data.Tests.Migrations;

// What a database of the run's provider holds, as lines that two databases are compared by: its tables; their columns with
// type, nullability, default and collation; keys, indexes with their filters, checks, foreign keys and triggers; on SQL
// Server READ_COMMITTED_SNAPSHOT; and the rows. Column order is left out, since a column added to a table comes last. EF's
// migrations lock table on SQLite is left out too: a script never creates it.
internal static partial class DatabaseCatalog
{
    private const string MigrationsLock = "__EFMigrationsLock";

    public static List<string> Of(TestDatabase database, bool historyRows)
    {
        IEnumerable<string> schema = TestProvider.Name switch
        {
            TestProvider.Sqlite => Sqlite(database),
            TestProvider.PostgreSql => database.ReadStrings(PostgreSql),
            _ => database.ReadStrings(SqlServer),
        };

        return [.. schema.Concat(Rows(database, historyRows)).Order(StringComparer.Ordinal)];
    }

    // The history tables hold the ids of the migrations that made them, which a test of what they made leaves out.
    private static IEnumerable<string> Rows(TestDatabase database, bool historyRows) =>
        database.Tables()
            .Where(table => table != MigrationsLock && (historyRows || !table.EndsWith("__migrations", StringComparison.Ordinal)))
            .SelectMany(table => database.ReadRows($"select * from {TestProvider.Quote(table)}").Select(row => $"row:{table}:{row}"));

    private static IEnumerable<string> Sqlite(TestDatabase database)
    {
        var tables = database.ReadStrings($"select name from sqlite_master where type = 'table' and name not like 'sqlite_%' and name <> '{MigrationsLock}'");
        foreach (var table in tables)
        {
            var sql = database.ReadStrings($"select sql from sqlite_master where type = 'table' and name = '{table}'").Single();
            yield return sql.StartsWith("CREATE VIRTUAL TABLE", StringComparison.OrdinalIgnoreCase) ? $"table:{table}:{sql}" : $"table:{table}";
            if (sql.Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"autoincrement:{table}";
            }

            foreach (var column in database.ReadStrings($"select name || ':' || type || ':notnull=' || \"notnull\" || ':default=' || ifnull(dflt_value, '') || ':pk=' || pk || ':hidden=' || hidden from pragma_table_xinfo('{table}')"))
            {
                yield return $"column:{table}.{column}";
            }

            foreach (var key in database.ReadStrings($"select '(' || group_concat(\"from\", ',') || ')->' || \"table\" || '(' || group_concat(\"to\", ',') || '):delete=' || on_delete || ':update=' || on_update from (select * from pragma_foreign_key_list('{table}') order by id, seq) group by id"))
            {
                yield return $"fk:{table}:{key}";
            }

            foreach (var check in Checks(sql))
            {
                yield return $"check:{table}.{check}";
            }
        }

        foreach (var item in database.ReadStrings("select type || ':' || tbl_name || '.' || name || ':' || sql from sqlite_master where type in ('index', 'trigger', 'view') and sql is not null"))
        {
            yield return item;
        }
    }

    // SQLite keeps a check only in the statement that created its table.
    private static IEnumerable<string> Checks(string sql)
    {
        foreach (Match match in CheckStart().Matches(sql))
        {
            var start = match.Index + match.Length;
            var end = start;
            for (var depth = 1; depth > 0 && end < sql.Length; end++)
            {
                depth += sql[end] switch { '(' => 1, ')' => -1, _ => 0 };
            }

            yield return $"{match.Groups["name"].Value}:{sql[start..(end - 1)]}";
        }
    }

    [GeneratedRegex("""(?:CONSTRAINT\s+"(?<name>[^"]+)"\s+)?CHECK\s*\(""", RegexOptions.IgnoreCase)]
    private static partial Regex CheckStart();

    // Every value is cast to text: "char" and name both convert to text implicitly, so || on them is ambiguous.
    private const string PostgreSql = """
        select 'table:' || c.relname::text || ':' || c.relkind::text
        from pg_class c join pg_namespace n on n.oid = c.relnamespace
        where n.nspname = current_schema() and c.relkind in ('r', 'p', 'v', 'm', 'S')
        union all
        select 'column:' || c.relname::text || '.' || a.attname::text || ':' || format_type(a.atttypid, a.atttypmod) || ':notnull=' || a.attnotnull::text
            || ':default=' || coalesce(pg_get_expr(d.adbin, d.adrelid), '') || ':identity=' || a.attidentity::text || ':generated=' || a.attgenerated::text
            || ':collation=' || coalesce(case when a.attcollation <> t.typcollation then co.collname::text end, '')
        from pg_attribute a
            join pg_class c on c.oid = a.attrelid
            join pg_namespace n on n.oid = c.relnamespace
            join pg_type t on t.oid = a.atttypid
            left join pg_attrdef d on d.adrelid = a.attrelid and d.adnum = a.attnum
            left join pg_collation co on co.oid = a.attcollation
        where n.nspname = current_schema() and c.relkind in ('r', 'p') and a.attnum > 0 and not a.attisdropped
        union all
        select 'constraint:' || c.relname::text || '.' || k.conname::text || ':' || pg_get_constraintdef(k.oid)
        from pg_constraint k join pg_class c on c.oid = k.conrelid join pg_namespace n on n.oid = c.relnamespace
        where n.nspname = current_schema()
        union all
        select 'index:' || tablename::text || '.' || indexname::text || ':' || indexdef from pg_indexes where schemaname = current_schema()
        union all
        select 'trigger:' || c.relname::text || '.' || g.tgname::text || ':' || pg_get_triggerdef(g.oid)
        from pg_trigger g join pg_class c on c.oid = g.tgrelid join pg_namespace n on n.oid = c.relnamespace
        where n.nspname = current_schema() and not g.tgisinternal
        """;

    // A constraint that SQL Server named itself has another name in each database, so only its definition counts. The
    // catalog's text is in collations of its own, so every value takes the database's before it is joined.
    private const string SqlServer = """
        SELECT CONCAT(N'table:', t.name COLLATE DATABASE_DEFAULT) FROM sys.tables t WHERE t.is_ms_shipped = 0
        UNION ALL
        SELECT CONCAT(N'column:', t.name COLLATE DATABASE_DEFAULT, N'.', c.name COLLATE DATABASE_DEFAULT, N':', TYPE_NAME(c.user_type_id) COLLATE DATABASE_DEFAULT,
            N'(', c.max_length, N',', c.precision, N',', c.scale, N'):nullable=', c.is_nullable, N':identity=', c.is_identity,
            N':default=', dc.definition COLLATE DATABASE_DEFAULT, IIF(dc.is_system_named = 0, CONCAT(N' as ', dc.name COLLATE DATABASE_DEFAULT), N''),
            N':computed=', cc.definition COLLATE DATABASE_DEFAULT, N':collation=', c.collation_name COLLATE DATABASE_DEFAULT)
        FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        WHERE t.is_ms_shipped = 0
        UNION ALL
        SELECT CONCAT(N'index:', t.name COLLATE DATABASE_DEFAULT, N'.', i.name COLLATE DATABASE_DEFAULT, N':', i.type_desc COLLATE DATABASE_DEFAULT,
            N':unique=', i.is_unique, N':pk=', i.is_primary_key, N':constraint=', i.is_unique_constraint,
            N':filter=', i.filter_definition COLLATE DATABASE_DEFAULT,
            N':keys=', (SELECT STRING_AGG(CONCAT(COL_NAME(ic.object_id, ic.column_id) COLLATE DATABASE_DEFAULT, IIF(ic.is_descending_key = 1, N' desc', N'')), N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
                FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0),
            N':include=', (SELECT STRING_AGG(COL_NAME(ic.object_id, ic.column_id) COLLATE DATABASE_DEFAULT, N',') WITHIN GROUP (ORDER BY ic.column_id)
                FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1))
        FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
        WHERE t.is_ms_shipped = 0 AND i.type > 0
        UNION ALL
        SELECT CONCAT(N'check:', OBJECT_NAME(k.parent_object_id) COLLATE DATABASE_DEFAULT, N'.', IIF(k.is_system_named = 1, N'', k.name COLLATE DATABASE_DEFAULT), N':',
            k.definition COLLATE DATABASE_DEFAULT, N':disabled=', k.is_disabled, N':untrusted=', k.is_not_trusted)
        FROM sys.check_constraints k
        UNION ALL
        SELECT CONCAT(N'fk:', OBJECT_NAME(f.parent_object_id) COLLATE DATABASE_DEFAULT, N'.', f.name COLLATE DATABASE_DEFAULT, N':',
            (SELECT STRING_AGG(COL_NAME(fc.parent_object_id, fc.parent_column_id) COLLATE DATABASE_DEFAULT, N',') WITHIN GROUP (ORDER BY fc.constraint_column_id) FROM sys.foreign_key_columns fc WHERE fc.constraint_object_id = f.object_id),
            N'->', OBJECT_NAME(f.referenced_object_id) COLLATE DATABASE_DEFAULT, N'(',
            (SELECT STRING_AGG(COL_NAME(fc.referenced_object_id, fc.referenced_column_id) COLLATE DATABASE_DEFAULT, N',') WITHIN GROUP (ORDER BY fc.constraint_column_id) FROM sys.foreign_key_columns fc WHERE fc.constraint_object_id = f.object_id),
            N'):delete=', f.delete_referential_action_desc COLLATE DATABASE_DEFAULT, N':update=', f.update_referential_action_desc COLLATE DATABASE_DEFAULT,
            N':disabled=', f.is_disabled, N':untrusted=', f.is_not_trusted)
        FROM sys.foreign_keys f
        UNION ALL
        SELECT CONCAT(N'trigger:', OBJECT_NAME(g.parent_id) COLLATE DATABASE_DEFAULT, N'.', g.name COLLATE DATABASE_DEFAULT, N':', OBJECT_DEFINITION(g.object_id) COLLATE DATABASE_DEFAULT)
        FROM sys.triggers g WHERE g.is_ms_shipped = 0
        UNION ALL
        SELECT CONCAT(N'option:read_committed_snapshot=', is_read_committed_snapshot_on) FROM sys.databases WHERE database_id = DB_ID()
        """;
}
