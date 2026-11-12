using System.Data.Common;

namespace FluentCMS.Core.Storage;

// What a database holds, as far as the migration runner compares it with the model.
public sealed record DatabaseSchema(IReadOnlyDictionary<string, DatabaseTable> Tables);

public sealed record DatabaseTable(string Name, IReadOnlyList<DatabaseColumn> Columns, IReadOnlyList<DatabaseIndex> Indexes)
{
    // Only where the provider keeps a column that a check constraint reads, which SQL Server does.
    public IReadOnlyList<DatabaseCheckConstraint> CheckConstraints { get; init; } = [];
}

public sealed record DatabaseColumn(string Name, string StoreType, bool IsNullable);

public sealed record DatabaseIndex(string Name, IReadOnlyList<string> Columns, bool IsUnique, bool IsPartial);

public sealed record DatabaseCheckConstraint(string Name, IReadOnlyList<string> Columns);

public interface IDatabaseSchemaReader
{
    // The connection is open and stays open.
    Task<DatabaseSchema> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default);
}
