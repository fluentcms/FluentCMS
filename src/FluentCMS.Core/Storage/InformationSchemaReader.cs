using System.Data.Common;
using System.Globalization;

namespace FluentCMS.Core.Storage;

// Reads tables and columns from the standard views. No standard view lists indexes, so a provider passes the query of its
// catalog that returns (table, index, unique, partial, column) in key order, and the one that returns (table, check
// constraint, column) where it keeps a column that a check constraint reads. Startup validation needs only the columns;
// schema apply compares the rest.
public sealed class InformationSchemaReader(string currentSchemaExpression, string? indexesSql = null, string? checkConstraintsSql = null) : IDatabaseSchemaReader
{
    public async Task<DatabaseSchema> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var columns = await ReadRowsAsync(
            connection,
            $"""
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
            FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = {currentSchemaExpression}
            """,
            reader => (Table: reader.GetString(0), Column: new DatabaseColumn(reader.GetString(1), StoreType(reader.GetString(2), Number(reader, 4), Number(reader, 5), Number(reader, 6)), reader.GetString(3) == "YES")),
            cancellationToken);

        var indexes = (indexesSql is null ? [] : await ReadRowsAsync(
                connection,
                indexesSql,
                reader => (Table: reader.GetString(0), Index: reader.GetString(1), Unique: reader.GetBoolean(2), Partial: reader.GetBoolean(3), Column: reader.GetString(4)),
                cancellationToken))
            .GroupBy(row => (row.Table, row.Index, row.Unique, row.Partial))
            .ToLookup(
                index => index.Key.Table,
                index => new DatabaseIndex(index.Key.Index, [.. index.Select(row => row.Column)], index.Key.Unique, index.Key.Partial),
                StringComparer.OrdinalIgnoreCase);

        var checks = (checkConstraintsSql is null ? [] : await ReadRowsAsync(
                connection,
                checkConstraintsSql,
                reader => (Table: reader.GetString(0), Check: reader.GetString(1), Column: reader.GetString(2)),
                cancellationToken))
            .GroupBy(row => (row.Table, row.Check))
            .ToLookup(
                check => check.Key.Table,
                check => new DatabaseCheckConstraint(check.Key.Check, [.. check.Select(row => row.Column)]),
                StringComparer.OrdinalIgnoreCase);

        return new DatabaseSchema(columns
            .GroupBy(column => column.Table, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                table => table.Key,
                table => new DatabaseTable(table.Key, [.. table.Select(column => column.Column)], [.. indexes[table.Key]]) { CheckConstraints = [.. checks[table.Key]] },
                StringComparer.OrdinalIgnoreCase));
    }

    // The type as EF writes it in the model: character varying(200), nvarchar(max), numeric(18,2). The views give a size
    // to other types too, such as the bits of an integer on PostgreSQL, which their name already says.
    private static string StoreType(string dataType, int? length, int? precision, int? scale) => dataType switch
    {
        "numeric" or "decimal" when precision is not null => $"{dataType}({precision},{scale ?? 0})",
        "character varying" or "character" or "varchar" or "nvarchar" or "char" or "nchar" or "varbinary" or "binary" when length is not null =>
            $"{dataType}({(length == -1 ? "max" : length.Value.ToString(CultureInfo.InvariantCulture))})",
        _ => dataType,
    };

    // PostgreSQL reports these as integers and SQL Server its precision as a tinyint.
    private static int? Number(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static async Task<List<T>> ReadRowsAsync<T>(DbConnection connection, string sql, Func<DbDataReader, T> read, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The expression and the queries are constants of the data provider package.
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
