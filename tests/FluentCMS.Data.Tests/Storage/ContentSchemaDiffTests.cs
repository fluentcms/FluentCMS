using System.Data.Common;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Sqlite;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

// The comparison of a column's collation, on SQLite with conventions that give content text one, as PostgreSQL and SQL
// Server do, and SQLite's reader with the collation and type a server's reader would report (#3476).
public sealed class ContentSchemaDiffTests
{
    private const string Collation = "NOCASE";

    private static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddCollection("article", type => type.Field("title", "text").Field("data", "json"))
        .Build();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AContentTextColumnOfAnotherCollationIsLeftAloneAndNamed()
    {
        using var database = new StorageDatabase(Schema, new CollatedConventions());
        using var context = database.CreateCreatedContext();

        var plan = await ContentSchemaDiff.PlanAsync(context, Schema, null, new ReportingReader(null), [], Token);

        plan.Steps.ShouldBeEmpty();
        plan.LeftAlone.ShouldBe([$"column title on table cms_article, whose collation is the database's default and not {Collation} as in the content model; schema apply does not change a collation, so a sort and a cursor on the column follow the one it has until it is altered by hand"]);
        (await ContentSchemaDiff.PlanAsync(context, Schema, null, new ReportingReader("RTRIM"), [], Token)).LeftAlone
            .ShouldHaveSingleItem().ShouldStartWith($"column title on table cms_article, whose collation is RTRIM and not {Collation} as in the content model;");

        // Schema sync, which runs on SQLite alone, whose model gives no collation, would only log it.
        (await ContentSchemaDiff.CompareAsync(context, new ReportingReader(null), [], Token)).NotApplied.ShouldBeEmpty();
    }

    [Fact]
    public async Task AColumnOfTheModelsCollationNamedInAnotherCaseIsNotListed()
    {
        using var database = new StorageDatabase(Schema, new CollatedConventions());
        using var context = database.CreateCreatedContext();

        var plan = await ContentSchemaDiff.PlanAsync(context, Schema, null, new ReportingReader(Collation.ToLowerInvariant()), [], Token);

        plan.Steps.ShouldBeEmpty();
        plan.LeftAlone.ShouldBeEmpty();
    }

    // The alter of a changed column gives it the model's collation, so the column is not listed as well.
    [Fact]
    public async Task AChangedColumnIsAlteredToTheModelsCollationAndNotListed()
    {
        using var database = new StorageDatabase(Schema, new CollatedConventions());
        using var context = database.CreateCreatedContext();

        var plan = await ContentSchemaDiff.PlanAsync(context, Schema, null, new ReportingReader(null, "INTEGER"), [], Token);

        var step = plan.Steps.ShouldHaveSingleItem();
        step.Kind.ShouldBe(SchemaStepKind.Destructive);
        step.Operations.Select(operation => operation.Operation).OfType<AlterColumnOperation>().ShouldHaveSingleItem().Collation.ShouldBe(Collation);
        plan.LeftAlone.ShouldBeEmpty();
    }

    private sealed class CollatedConventions : StorageConventions
    {
        public override void ConfigureTextColumn(EntityTypeBuilder entity, PropertyBuilder column) => column.UseCollation(Collation);
    }

    // SQLite's reader reads no collation; this one reports the given one, and type, for cms_article.title.
    private sealed class ReportingReader(string? collation, string? storeType = null) : IDatabaseSchemaReader
    {
        private readonly IDatabaseSchemaReader _inner = new SqliteDataProvider("Data Source=:memory:").SchemaReader;

        public async Task<DatabaseSchema> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default)
        {
            var database = await _inner.ReadAsync(connection, cancellationToken);
            return new DatabaseSchema(database.Tables.ToDictionary(
                table => table.Key,
                table => table.Key != "cms_article" ? table.Value : table.Value with
                {
                    Columns = [.. table.Value.Columns.Select(column => column.Name != "title" ? column : column with { Collation = collation, StoreType = storeType ?? column.StoreType })],
                },
                StringComparer.OrdinalIgnoreCase));
        }
    }
}
