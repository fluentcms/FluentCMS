using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace FluentCMS.Core.Storage;

internal enum SchemaStepKind
{
    Add,
    Rename,
    Index,
    Destructive,

    // What schema apply cannot do, such as a required column with no value for the rows a table has.
    Refused,
}

// One change schema apply makes, in the words schema sync logs, with its operations and the phase each one runs in.
internal sealed record SchemaApplyStep(string Description, SchemaStepKind Kind, IReadOnlyList<(int Phase, MigrationOperation Operation)> Operations)
{
    public bool IsDestructive => Kind == SchemaStepKind.Destructive;
}

// The content model against the database. It starts from the operations that would create the whole content model,
// which leave out what ExcludeFromMigrations marks, and keeps the ones the database has not got.
internal sealed class ContentSchemaDiff
{
    // The order schema apply runs its operations in: each frees a name or adds a column that a later one needs.
    private const int DropIndexPhase = 0;
    private const int DropCheckPhase = 1;
    private const int RenamePhase = 2;
    private const int DropColumnPhase = 3;
    private const int AlterPhase = 4;
    private const int AddColumnPhase = 5;
    private const int CreateTablePhase = 6;
    private const int AddCheckPhase = 7;
    private const int CreateIndexPhase = 8;
    private const int DropTablePhase = 9;

    private readonly List<MigrationOperation> _additive = [];
    private readonly List<string> _missing = [];
    private readonly List<string> _notApplied = [];
    private readonly List<SchemaApplyStep> _steps = [];
    private readonly Dictionary<string, Dictionary<string, StoredField>>? _fields;
    private string? _firstMissingName;

    private ContentSchemaDiff(Dictionary<string, Dictionary<string, StoredField>>? fields) => _fields = fields;

    // What schema sync may apply.
    public IReadOnlyList<MigrationOperation> Additive => _additive;

    // What schema sync must only report.
    public IReadOnlyList<string> NotApplied => _notApplied;

    // What startup validation fails on, in the words of FCMS1301.
    public IReadOnlyList<string> Missing => _missing;

    // A name for the migration that fixes the first of Missing.
    public string SuggestedMigrationName => $"Add{_firstMissingName}";

    // What schema apply does, from PlanAsync only.
    public IReadOnlyList<SchemaApplyStep> Steps => _steps;

    // The operations of the steps in the order they run.
    public static IReadOnlyList<MigrationOperation> OperationsOf(IEnumerable<SchemaApplyStep> steps) =>
        [.. steps.SelectMany(step => step.Operations).OrderBy(operation => operation.Phase).Select(operation => operation.Operation)];

    // A table under a plugin's prefix belongs to the plugin, whether it maps the table, migrates it or neither.
    public static Task<ContentSchemaDiff> CompareAsync(CmsDbContext context, IDatabaseSchemaReader reader, IReadOnlyCollection<string> pluginTablePrefixes, CancellationToken cancellationToken) =>
        CompareAsync(new ContentSchemaDiff(null), context, reader, pluginTablePrefixes, cancellationToken);

    // The same comparison with the steps of schema apply, which also renames a column of a field's former name, moves the
    // references kept under its former path, drops what the schema no longer has and changes what differs. The context's
    // model must be the schema's.
    public static async Task<ContentSchemaDiff> PlanAsync(CmsDbContext context, SchemaModel schema, IDatabaseSchemaReader reader, IReadOnlyCollection<string> pluginTablePrefixes, CancellationToken cancellationToken)
    {
        var diff = await CompareAsync(new ContentSchemaDiff(FieldsByTable(schema)), context, reader, pluginTablePrefixes, cancellationToken);
        await diff.PlanReferencesAsync(context, schema, cancellationToken);
        return diff;
    }

    private static async Task<ContentSchemaDiff> CompareAsync(ContentSchemaDiff diff, CmsDbContext context, IDatabaseSchemaReader reader, IReadOnlyCollection<string> pluginTablePrefixes, CancellationToken cancellationToken)
    {
        var model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model);
        var database = await ReadDatabaseAsync(context, reader, cancellationToken);

        var indexes = operations.OfType<CreateIndexOperation>().ToLookup(index => index.Table);
        foreach (var create in operations.OfType<CreateTableOperation>())
        {
            diff.CompareTable(create, database.Tables.GetValueOrDefault(create.Name), [.. indexes[create.Name]]);
        }

        var known = model.Tables.Select(table => table.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var table in database.Tables.Keys.Where(table => IsStaleContentTable(table, known, pluginTablePrefixes)).Order(StringComparer.Ordinal))
        {
            diff._notApplied.Add($"drop table {table}");
            diff.Step($"drop table {table}", SchemaStepKind.Destructive, (DropTablePhase, new DropTableOperation { Name = table }));
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

    // The stored fields of every content table by column, which tell schema apply a field's former columns and its empty value.
    private static Dictionary<string, Dictionary<string, StoredField>> FieldsByTable(SchemaModel schema)
    {
        var tables = new Dictionary<string, Dictionary<string, StoredField>>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in schema.ContentTypes)
        {
            foreach (var field in FieldStorage.Of(schema, type).Where(field => field.Kind != FieldStorageKind.Reference))
            {
                var table = field.Localized ? StorageNames.LocaleTable(type) : StorageNames.Table(type);
                if (!tables.TryGetValue(table, out var columns))
                {
                    tables[table] = columns = new Dictionary<string, StoredField>(StringComparer.OrdinalIgnoreCase);
                }

                columns[field.Column] = field;
            }
        }

        return tables;
    }

    // References that cms_reference keeps under a path of a field's former name, or of a component above it, which the
    // schema no longer has, move to the field's path.
    private async Task PlanReferencesAsync(CmsDbContext context, SchemaModel schema, CancellationToken cancellationToken)
    {
        foreach (var type in schema.ContentTypes)
        {
            var paths = FieldStorage.ByPath(schema, type);
            var moved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var leaf in FieldStorage.Of(schema, type).Where(ContentRenames.HoldsReferences))
            {
                string[] formers = [.. leaf.FormerPaths.Where(former => !paths.ContainsKey(former) && !moved.Contains(former))];
                if (formers.Length == 0)
                {
                    continue;
                }

                var stored = await context.References
                    .Where(reference => reference.SourceType == type.Name && formers.Contains(reference.Field))
                    .Select(reference => reference.Field)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                if (formers.FirstOrDefault(stored.Contains) is not { } former)
                {
                    continue;
                }

                var path = leaf.Location[(type.Name.Length + 1)..];
                moved.Add(former);
                Step(
                    $"rename field {type.Name}.{former} to {type.Name}.{path} in table {StorageNames.Reference}",
                    SchemaStepKind.Rename,
                    (RenamePhase, ContentRenames.MoveReferences(type.Name, former, path)));
            }
        }
    }

    private bool Applying => _fields is not null;

    private void CompareTable(CreateTableOperation create, DatabaseTable? table, List<CreateIndexOperation> indexes)
    {
        if (table is null)
        {
            _additive.Add(create);
            _additive.AddRange(indexes);
            AddMissing($"table {create.Name} is missing", create.Name.Replace(StorageNames.Prefix, string.Empty, StringComparison.Ordinal));
            Step($"create table {create.Name}", SchemaStepKind.Add, (CreateTablePhase, create));
            foreach (var index in indexes)
            {
                Step($"create index {index.Name} on table {create.Name}", SchemaStepKind.Add, (CreateIndexPhase, index));
            }

            return;
        }

        var existing = table.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var wantedNames = create.Columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columnsAfterSync = existing.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var wanted in create.Columns)
        {
            if (existing.TryGetValue(wanted.Name, out var column))
            {
                CompareColumn(create, table, wanted, column);
            }
            else if (FormerColumnOf(create, wanted, existing, wantedNames, renamed) is { } former)
            {
                renamed[former.Name] = wanted.Name;
                columnsAfterSync.Add(wanted.Name);

                // A check named for the former column is made again under the model's name, unless the column is changed,
                // which does that already. Only SQL Server's reader reports checks.
                var checks = Differs(wanted, former) ? [] : DropChecks(table, former.Name).ToList();
                Step(
                    $"rename column {former.Name} to {wanted.Name} on table {create.Name}",
                    SchemaStepKind.Rename,
                    [
                        (RenamePhase, new RenameColumnOperation { Table = create.Name, Schema = create.Schema, Name = former.Name, NewName = wanted.Name }),
                        .. checks,
                        .. checks.Count == 0 ? [] : AddChecks(create.CheckConstraints.Where(check => References(check, wanted.Name))),
                    ]);
                CompareColumn(create, table, wanted, former);
            }
            else if (CompareMissingColumn(create, wanted))
            {
                columnsAfterSync.Add(wanted.Name);
            }
        }

        foreach (var extra in table.Columns.Where(column => !wantedNames.Contains(column.Name) && !renamed.ContainsKey(column.Name)))
        {
            var description = $"drop column {extra.Name} from table {create.Name}";
            _notApplied.Add(description);
            Step(description, SchemaStepKind.Destructive, [.. DropChecks(table, extra.Name), (DropColumnPhase, new DropColumnOperation { Table = create.Name, Schema = create.Schema, Name = extra.Name })]);
        }

        CompareIndexes(create, table, indexes, columnsAfterSync, renamed);
    }

    // A column of one of the field's former names that the database has and the schema does not, when schema apply plans.
    private DatabaseColumn? FormerColumnOf(CreateTableOperation create, AddColumnOperation wanted, Dictionary<string, DatabaseColumn> existing, HashSet<string> wantedNames, Dictionary<string, string> renamed)
    {
        if (_fields?.GetValueOrDefault(create.Name)?.GetValueOrDefault(wanted.Name) is not { } field)
        {
            return null;
        }

        return field.FormerColumns
            .Where(former => !wantedNames.Contains(former) && !renamed.ContainsKey(former))
            .Select(former => existing.GetValueOrDefault(former))
            .FirstOrDefault(column => column is not null);
    }

    private bool CompareMissingColumn(CreateTableOperation create, AddColumnOperation wanted)
    {
        AddMissing($"table {create.Name} is missing column {wanted.Name}", wanted.Name);

        // SQLite adds a constraint only by rebuilding the table, and a rebuild from the model would drop the columns
        // the model no longer has. Schema sync runs on SQLite only, which is where this quoting comes from.
        var checks = create.CheckConstraints.Where(check => References(check, wanted.Name)).ToList();

        if (!wanted.IsNullable && wanted.DefaultValue is null && wanted.DefaultValueSql is null)
        {
            var description = $"add required column {wanted.Name} with no default to table {create.Name}";
            _notApplied.Add(description);

            // The rows a table has get the value a read gives a field that has none.
            if (EmptyValueOf(create, wanted) is not { } empty)
            {
                Step(description, SchemaStepKind.Refused);
                return false;
            }

            wanted.DefaultValue = empty;
            Step($"add column {wanted.Name} to table {create.Name}, empty in the rows it has", SchemaStepKind.Add, [(AddColumnPhase, wanted), .. AddChecks(checks)]);
            return true;
        }

        _additive.Add(wanted);
        foreach (var check in checks)
        {
            _notApplied.Add($"add check constraint {check.Name} to table {create.Name}");
        }

        Step($"add column {wanted.Name} to table {create.Name}", SchemaStepKind.Add, [(AddColumnPhase, wanted), .. AddChecks(checks)]);
        return true;
    }

    // What EntryStore reads for a JSON field with no value, and '' for text. Null for anything else, which has no empty value.
    private string? EmptyValueOf(CreateTableOperation create, AddColumnOperation wanted) =>
        _fields?.GetValueOrDefault(create.Name)?.GetValueOrDefault(wanted.Name) switch
        {
            { Kind: FieldStorageKind.Json, Field.FieldType: FieldTypeNames.RichText } => RichText.Empty.Json,
            { Kind: FieldStorageKind.Json } stored => stored.DefaultValue ?? "[]",
            { ClrType: var type } when type == typeof(string) => "",
            _ => null,
        };

    private static bool Differs(AddColumnOperation wanted, DatabaseColumn existing) =>
        !Same(wanted.ColumnType, existing.StoreType) || wanted.IsNullable != existing.IsNullable;

    private void CompareColumn(CreateTableOperation create, DatabaseTable table, AddColumnOperation wanted, DatabaseColumn existing)
    {
        if (Differs(wanted, existing))
        {
            var description = $"change column {wanted.Name} on table {wanted.Table} from {Describe(existing.StoreType, existing.IsNullable)} to {Describe(wanted.ColumnType, wanted.IsNullable)}";
            _notApplied.Add(description);
            Step(
                description,
                SchemaStepKind.Destructive,
                [
                    .. DropChecks(table, existing.Name),
                    (AlterPhase, Alter(wanted, existing)),
                    .. AddChecks(create.CheckConstraints.Where(check => References(check, wanted.Name))),
                ]);
        }
    }

    private void CompareIndexes(CreateTableOperation create, DatabaseTable existing, List<CreateIndexOperation> wanted, HashSet<string> columnsAfterSync, Dictionary<string, string> renamed)
    {
        var table = create.Name;
        foreach (var index in wanted)
        {
            var current = existing.Indexes.FirstOrDefault(candidate => Same(candidate.Name, index.Name));
            if (current is null)
            {
                if (index.Columns.All(columnsAfterSync.Contains))
                {
                    _additive.Add(index);
                    Step($"create index {index.Name} on table {table}", SchemaStepKind.Index, (CreateIndexPhase, index));
                }
            }
            else if (!current.Columns.Select(column => renamed.GetValueOrDefault(column, column)).SequenceEqual(index.Columns, StringComparer.OrdinalIgnoreCase)
                || current.IsUnique != index.IsUnique
                || current.IsPartial != (index.Filter is not null))
            {
                var description = $"change index {index.Name} on table {table}";
                _notApplied.Add(description);
                Step(description, SchemaStepKind.Index, (DropIndexPhase, DropIndex(create, current.Name)), (CreateIndexPhase, index));
            }
        }

        foreach (var extra in existing.Indexes.Where(index => !wanted.Any(other => Same(other.Name, index.Name))))
        {
            var description = $"drop index {extra.Name} from table {table}";
            _notApplied.Add(description);
            Step(description, SchemaStepKind.Index, (DropIndexPhase, DropIndex(create, extra.Name)));
        }
    }

    private void Step(string description, SchemaStepKind kind, params (int Phase, MigrationOperation Operation)[] operations)
    {
        if (Applying)
        {
            _steps.Add(new SchemaApplyStep(description, kind, operations));
        }
    }

    private void AddMissing(string description, string name)
    {
        _missing.Add(description);
        _firstMissingName ??= string.Concat(name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static DropIndexOperation DropIndex(CreateTableOperation create, string name) => new() { Table = create.Name, Schema = create.Schema, Name = name };

    // SQL Server keeps a column that a check constraint reads; the other readers report none, and their providers drop them with the column.
    private static IEnumerable<(int, MigrationOperation)> DropChecks(DatabaseTable table, string column) =>
        table.CheckConstraints
            .Where(check => check.Columns.Contains(column, StringComparer.OrdinalIgnoreCase))
            .Select(check => (DropCheckPhase, (MigrationOperation)new DropCheckConstraintOperation { Table = table.Name, Name = check.Name }));

    private static IEnumerable<(int, MigrationOperation)> AddChecks(IEnumerable<AddCheckConstraintOperation> checks) =>
        checks.Select(check => (AddCheckPhase, (MigrationOperation)check));

    // SQLite and PostgreSQL quote a name in double quotes, SQL Server in brackets.
    private static bool References(AddCheckConstraintOperation check, string column) =>
        check.Sql.Contains($"\"{column}\"", StringComparison.Ordinal) || check.Sql.Contains($"[{column}]", StringComparison.Ordinal);

    private static AlterColumnOperation Alter(AddColumnOperation wanted, DatabaseColumn existing)
    {
        var alter = new AlterColumnOperation
        {
            Schema = wanted.Schema,
            Table = wanted.Table,
            Name = wanted.Name,
            ClrType = wanted.ClrType,
            ColumnType = wanted.ColumnType,
            IsUnicode = wanted.IsUnicode,
            IsFixedLength = wanted.IsFixedLength,
            MaxLength = wanted.MaxLength,
            Precision = wanted.Precision,
            Scale = wanted.Scale,
            IsNullable = wanted.IsNullable,
            DefaultValue = wanted.DefaultValue,
            DefaultValueSql = wanted.DefaultValueSql,
            Collation = wanted.Collation,
        };
        alter.AddAnnotations(wanted.GetAnnotations());
        alter.OldColumn.ClrType = wanted.ClrType;
        alter.OldColumn.ColumnType = existing.StoreType;
        alter.OldColumn.IsNullable = existing.IsNullable;
        return alter;
    }

    private static bool IsStaleContentTable(string table, HashSet<string> known, IReadOnlyCollection<string> pluginTablePrefixes) =>
        table.StartsWith(StorageNames.Prefix, StringComparison.Ordinal)
        && !table.EndsWith(StorageNames.HistoryTableSuffix, StringComparison.Ordinal)
        && !known.Contains(table)
        && !StorageNames.SystemTables.Contains(table)
        && !pluginTablePrefixes.Any(prefix => table.StartsWith(prefix, StringComparison.Ordinal));

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Describe(string? storeType, bool isNullable) => $"{storeType} {(isNullable ? "NULL" : "NOT NULL")}";
}
