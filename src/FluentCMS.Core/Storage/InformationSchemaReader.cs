using System.Data.Common;
using System.Globalization;

namespace FluentCMS.Core.Storage;

// Reads tables and columns from the standard views. No indexes: startup validation needs only the columns, and
// schema sync, which compares indexes, runs on SQLite only.
public sealed class InformationSchemaReader(string currentSchemaExpression) : IDatabaseSchemaReader
{
    public async Task<DatabaseSchema> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The expression is a constant of the data provider package.
        command.CommandText = $"""
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
            FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = {currentSchemaExpression}
            """;
#pragma warning restore CA2100

        var columns = new List<(string Table, DatabaseColumn Column)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var storeType = StoreType(reader.GetString(2), Number(reader, 4), Number(reader, 5), Number(reader, 6));
            columns.Add((reader.GetString(0), new DatabaseColumn(reader.GetString(1), storeType, reader.GetString(3) == "YES")));
        }

        return new DatabaseSchema(columns
            .GroupBy(column => column.Table, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                table => table.Key,
                table => new DatabaseTable(table.Key, [.. table.Select(column => column.Column)], []),
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
}
