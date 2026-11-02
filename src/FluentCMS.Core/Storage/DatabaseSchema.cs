using System.Data.Common;

namespace FluentCMS.Core.Storage;

// What a database holds, as far as the migration runner compares it with the model.
public sealed record DatabaseSchema(IReadOnlyDictionary<string, DatabaseTable> Tables);

public sealed record DatabaseTable(string Name, IReadOnlyList<DatabaseColumn> Columns, IReadOnlyList<DatabaseIndex> Indexes);

public sealed record DatabaseColumn(string Name, string StoreType, bool IsNullable);

public sealed record DatabaseIndex(string Name, IReadOnlyList<string> Columns, bool IsUnique, bool IsPartial);

public interface IDatabaseSchemaReader
{
    // The connection is open and stays open.
    Task<DatabaseSchema> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default);
}
