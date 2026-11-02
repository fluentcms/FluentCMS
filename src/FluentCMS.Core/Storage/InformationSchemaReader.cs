using System.Data.Common;

namespace FluentCMS.Core.Storage;

// Reads tables and columns from the standard views. No indexes: startup validation needs only the columns, and
// schema sync, which compares indexes, runs on SQLite only. Never run on a server until #2711.
public sealed class InformationSchemaReader(string currentSchemaExpression) : IDatabaseSchemaReader
{
    public async Task<DatabaseSchema> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The expression is a constant of the data provider package.
        command.CommandText = $"SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = {currentSchemaExpression}";
#pragma warning restore CA2100

        var columns = new List<(string Table, DatabaseColumn Column)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add((reader.GetString(0), new DatabaseColumn(reader.GetString(1), reader.GetString(2), reader.GetString(3) == "YES")));
        }

        return new DatabaseSchema(columns
            .GroupBy(column => column.Table, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                table => table.Key,
                table => new DatabaseTable(table.Key, [.. table.Select(column => column.Column)], []),
                StringComparer.OrdinalIgnoreCase));
    }
}
