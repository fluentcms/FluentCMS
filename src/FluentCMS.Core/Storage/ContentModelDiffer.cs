using System.Text.Json;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.Update.Internal;

namespace FluentCMS.Core.Storage;

// EF's differ, which takes a column for the same column only by its name or by a field's former name, and moves the paths
// that cms_reference keeps references under. EF would otherwise take a dropped column and an added one of the same store
// type for a rename, so a removed field could hand its values to an unrelated new one. The renames come from the schemas
// on the two models, as `migrations add` prints them, so the generated migration carries what the review names.
//
// EF has no public seam for how columns are paired, so this derives from its internal differ. It is used only when the
// tool scaffolds a content migration, with the tool's own EF Core.
#pragma warning disable EF1001
internal sealed class ContentModelDiffer(
    IRelationalTypeMappingSource typeMappingSource,
    IMigrationsAnnotationProvider migrationsAnnotationProvider,
    IRelationalAnnotationProvider relationalAnnotationProvider,
    IRowIdentityMapFactory rowIdentityMapFactory,
    CommandBatchPreparerDependencies commandBatchPreparerDependencies,
    SchemaModel schema)
    : MigrationsModelDiffer(typeMappingSource, migrationsAnnotationProvider, relationalAnnotationProvider, rowIdentityMapFactory, commandBatchPreparerDependencies)
{
    private static readonly ILookup<string, (string From, string To)> NoColumns = Array.Empty<ColumnRename>().ToLookup(rename => rename.Table, rename => (rename.From, rename.To));

    public static ContentModelDiffer For(CmsDbContext context) => new(
        context.GetService<IRelationalTypeMappingSource>(),
        context.GetService<IMigrationsAnnotationProvider>(),
        context.GetService<IRelationalAnnotationProvider>(),
        context.GetService<IRowIdentityMapFactory>(),
        context.GetService<CommandBatchPreparerDependencies>(),
        context.Schema);

    // The column renames of the diff that runs, from its source to its target, by table.
    private ILookup<string, (string From, string To)> _columns = NoColumns;

    public override bool HasDifferences(IRelationalModel? source, IRelationalModel? target) =>
        Renaming(source, target, paths => paths.Count > 0 || base.HasDifferences(source, target));

    public override IReadOnlyList<MigrationOperation> GetDifferences(IRelationalModel? source, IRelationalModel? target) =>
        Renaming(source, target, paths =>
        {
            var operations = base.GetDifferences(source, target);
            return paths.Count == 0 ? operations : [.. operations, .. paths.Select(path => ContentRenames.MoveReferences(path.Type, path.From, path.To))];
        });

    // A content table pairs its columns by name, then by the former name of a renamed field; the rest are dropped and added.
    protected override IEnumerable<MigrationOperation> Diff(IEnumerable<IColumn> source, IEnumerable<IColumn> target, DiffContext diffContext)
    {
        var table = target.FirstOrDefault()?.Table ?? source.FirstOrDefault()?.Table;
        if (table is null || table.IsExcludedFromMigrations)
        {
            return base.Diff(source, target, diffContext);
        }

        var renames = _columns[table.Name].ToHashSet();
        return DiffCollection(
            source,
            target,
            diffContext,
            Diff,
            (column, context) => Add(column, context),
            Remove,
            (s, t, _) => string.Equals(s.Name, t.Name, StringComparison.OrdinalIgnoreCase),
            (s, t, _) => renames.Contains((s.Name, t.Name)));
    }

    // One model is the schema's and the other carries the schema it was generated from: a model snapshot when a migration's
    // Up is scaffolded, and the target of its Down the other way round. A model without a schema, or with one that cannot be
    // read, renames nothing.
    private T Renaming<T>(IRelationalModel? source, IRelationalModel? target, Func<IReadOnlyList<PathRename>, T> diff)
    {
        var current = SchemaSnapshot.Of(schema).Json;
        var (previous, reverse) = SchemaOf(target) == current ? (SchemaOf(source), false)
            : SchemaOf(source) == current ? (SchemaOf(target), true)
            : (null, false);
        var renames = previous is null ? ContentRenames.None : RenamesFrom(previous);

        _columns = renames.Columns.ToLookup(rename => rename.Table, rename => reverse ? (rename.To, rename.From) : (rename.From, rename.To), StringComparer.OrdinalIgnoreCase);
        try
        {
            return diff([.. renames.Paths.Select(path => reverse ? path with { From = path.To, To = path.From } : path)]);
        }
        finally
        {
            _columns = NoColumns;
        }
    }

    private ContentRenames RenamesFrom(string previous)
    {
        try
        {
            return ContentRenames.Of(schema, SchemaSnapshotDiff.Compare(previous, SchemaSnapshot.Of(schema).Json));
        }
        catch (JsonException)
        {
            return ContentRenames.None;
        }
    }

    private static string? SchemaOf(IRelationalModel? model) => model?.Model.FindAnnotation(CmsDbContext.SchemaAnnotation)?.Value as string;
}
#pragma warning restore EF1001
