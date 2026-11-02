using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace FluentCMS.Core.Storage;

// The content model against the database. It starts from the operations that would create the whole content model,
// which leave out what ExcludeFromMigrations marks, and keeps the ones the database has not got.
internal sealed class ContentSchemaDiff
{
    private readonly List<MigrationOperation> _additive = [];
    private readonly List<string> _missing = [];
    private readonly List<string> _notApplied = [];
    private string? _firstMissingName;

    // What schema sync may apply.
    public IReadOnlyList<MigrationOperation> Additive => _additive;

    // What schema sync must only report.
    public IReadOnlyList<string> NotApplied => _notApplied;

    // What startup validation fails on, in the words of FCMS1301.
    public IReadOnlyList<string> Missing => _missing;

    // A name for the migration that fixes the first of Missing.
    public string SuggestedMigrationName => $"Add{_firstMissingName}";

    public static async Task<ContentSchemaDiff> CompareAsync(CmsDbContext context, IDatabaseSchemaReader reader, CancellationToken cancellationToken)
    {
        var model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model);
        var database = await ReadDatabaseAsync(context, reader, cancellationToken);

        var diff = new ContentSchemaDiff();
        var indexes = operations.OfType<CreateIndexOperation>().ToLookup(index => index.Table);
        foreach (var create in operations.OfType<CreateTableOperation>())
        {
            diff.CompareTable(create, database.Tables.GetValueOrDefault(create.Name), [.. indexes[create.Name]]);
        }

        var known = model.Tables.Select(table => table.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var table in database.Tables.Keys.Where(table => IsStaleContentTable(table, known)).Order(StringComparer.Ordinal))
        {
            diff._notApplied.Add($"drop table {table}");
        }

        return diff;
    }

    private static async Task<DatabaseSchema> ReadDatabaseAsync(CmsDbContext context, IDatabaseSchemaReader reader, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            return await reader.ReadAsync(context.Database.GetDbConnection(), cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private void CompareTable(CreateTableOperation create, DatabaseTable? table, List<CreateIndexOperation> indexes)
    {
        if (table is null)
        {
            _additive.Add(create);
            _additive.AddRange(indexes);
            AddMissing($"table {create.Name} is missing", create.Name.Replace(StorageNames.Prefix, string.Empty, StringComparison.Ordinal));
            return;
        }

        var existing = table.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var columnsAfterSync = existing.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var wanted in create.Columns)
        {
            if (existing.TryGetValue(wanted.Name, out var column))
            {
                CompareColumn(wanted, column);
            }
            else if (CompareMissingColumn(create, wanted))
            {
                columnsAfterSync.Add(wanted.Name);
            }
        }

        foreach (var extra in table.Columns.Where(column => !create.Columns.Any(wanted => Same(wanted.Name, column.Name))))
        {
            _notApplied.Add($"drop column {extra.Name} from table {create.Name}");
        }

        CompareIndexes(create.Name, table, indexes, columnsAfterSync);
    }

    private bool CompareMissingColumn(CreateTableOperation create, AddColumnOperation wanted)
    {
        AddMissing($"table {create.Name} is missing column {wanted.Name}", wanted.Name);

        if (!wanted.IsNullable && wanted.DefaultValue is null && wanted.DefaultValueSql is null)
        {
            _notApplied.Add($"add required column {wanted.Name} with no default to table {create.Name}");
            return false;
        }

        _additive.Add(wanted);

        // SQLite adds a constraint only by rebuilding the table, and a rebuild from the model would drop the columns
        // the model no longer has. Schema sync runs on SQLite only, which is where this quoting comes from.
        foreach (var check in create.CheckConstraints.Where(check => check.Sql.Contains($"\"{wanted.Name}\"", StringComparison.Ordinal)))
        {
            _notApplied.Add($"add check constraint {check.Name} to table {create.Name}");
        }

        return true;
    }

    private void CompareColumn(AddColumnOperation wanted, DatabaseColumn existing)
    {
        if (!Same(wanted.ColumnType, existing.StoreType) || wanted.IsNullable != existing.IsNullable)
        {
            _notApplied.Add($"change column {wanted.Name} on table {wanted.Table} from {Describe(existing.StoreType, existing.IsNullable)} to {Describe(wanted.ColumnType, wanted.IsNullable)}");
        }
    }

    private void CompareIndexes(string table, DatabaseTable existing, List<CreateIndexOperation> wanted, HashSet<string> columnsAfterSync)
    {
        foreach (var index in wanted)
        {
            var current = existing.Indexes.FirstOrDefault(candidate => Same(candidate.Name, index.Name));
            if (current is null)
            {
                if (index.Columns.All(columnsAfterSync.Contains))
                {
                    _additive.Add(index);
                }
            }
            else if (!current.Columns.SequenceEqual(index.Columns, StringComparer.OrdinalIgnoreCase) || current.IsUnique != index.IsUnique || current.IsPartial != (index.Filter is not null))
            {
                _notApplied.Add($"change index {index.Name} on table {table}");
            }
        }

        foreach (var extra in existing.Indexes.Where(index => !wanted.Any(other => Same(other.Name, index.Name))))
        {
            _notApplied.Add($"drop index {extra.Name} from table {table}");
        }
    }

    private void AddMissing(string description, string name)
    {
        _missing.Add(description);
        _firstMissingName ??= string.Concat(name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static bool IsStaleContentTable(string table, HashSet<string> known) =>
        table.StartsWith(StorageNames.Prefix, StringComparison.Ordinal)
        && !table.EndsWith(StorageNames.HistoryTableSuffix, StringComparison.Ordinal)
        && !known.Contains(table)
        && !StorageNames.SystemTables.Contains(table);

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Describe(string? storeType, bool isNullable) => $"{storeType} {(isNullable ? "NULL" : "NOT NULL")}";
}
