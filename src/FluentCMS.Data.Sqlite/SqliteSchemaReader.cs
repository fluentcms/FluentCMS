using System.Data.Common;
using FluentCMS.Core.Storage;

namespace FluentCMS.Data.Sqlite;

internal sealed class SqliteSchemaReader : IDatabaseSchemaReader
{
    private const string ColumnsSql = """
        select m.name, c.name, c.type, c."notnull"
        from sqlite_master m join pragma_table_info(m.name) c
        where m.type = 'table' and m.name not like 'sqlite_%'
        """;

    // Origin 'c' is an index from CREATE INDEX, not one SQLite made for a key.
    private const string IndexesSql = """
        select m.name, i.name, i."unique", i.partial, c.name
        from sqlite_master m join pragma_index_list(m.name) i join pragma_index_info(i.name) c
        where m.type = 'table' and m.name not like 'sqlite_%' and i.origin = 'c'
        order by m.name, i.name, c.seqno
        """;

    public async Task<DatabaseSchema> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var columns = await ReadRowsAsync(
            connection,
            ColumnsSql,
            reader => (Table: reader.GetString(0), Column: new DatabaseColumn(reader.GetString(1), reader.GetString(2), reader.GetInt64(3) == 0)),
            cancellationToken);

        var indexColumns = await ReadRowsAsync(
            connection,
            IndexesSql,
            reader => (Table: reader.GetString(0), Index: reader.GetString(1), Unique: reader.GetInt64(2) == 1, Partial: reader.GetInt64(3) == 1, Column: reader.GetString(4)),
            cancellationToken);

        var indexes = indexColumns
            .GroupBy(row => (row.Table, row.Index, row.Unique, row.Partial))
            .ToLookup(
                index => index.Key.Table,
                index => new DatabaseIndex(index.Key.Index, [.. index.Select(row => row.Column)], index.Key.Unique, index.Key.Partial),
                StringComparer.OrdinalIgnoreCase);

        return new DatabaseSchema(columns
            .GroupBy(column => column.Table, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                table => table.Key,
                table => new DatabaseTable(table.Key, [.. table.Select(column => column.Column)], [.. indexes[table.Key]]),
                StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<List<T>> ReadRowsAsync<T>(DbConnection connection, string sql, Func<DbDataReader, T> read, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constants of this class.
        command.CommandText = sql;
#pragma warning restore CA2100

        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }
}
