using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Storage.Spike.Tests;

public sealed class JsonFilterTests : IDisposable
{
    private readonly SpikeDatabase _db = new();
    private readonly SpikeDbContext _context;
    private readonly Guid _heroDocument;
    private readonly Guid _quoteDocument;
    private readonly Guid _emptyDocument;

    public JsonFilterTests()
    {
        _context = _db.CreateMigratedContext();
        _heroDocument = _context.AddShared(
            sections: """[{"type":"hero","heading":"Welcome"},{"type":"quote","text":"Hi"}]""",
            metadata: """{"audience":"developers","minutes":12,"labels":["net","sql"]}""");
        _quoteDocument = _context.AddShared(
            sections: """[{"type":"quote","text":"Welcome"}]""",
            metadata: """{"audience":"o'brien","minutes":3,"labels":["ops"]}""");
        _emptyDocument = _context.AddShared(sections: "[]");
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _db.Dispose();
    }

    private IQueryable<Dictionary<string, object>> Articles => _context.Rows("cms_article");

    private static List<Guid> Ids(IQueryable<Dictionary<string, object>> query) =>
        query.Select(r => EF.Property<Guid>(r, "document_id")).ToList();

    private static string Sql(IQueryable query) => query.ToQueryString().ReplaceLineEndings("\n");

    [Fact]
    public void BlockTypePresenceTranslatesToExistsOverJsonEach()
    {
        var blockType = "hero";
        var query = Articles.Where(r => _context.JsonEach(EF.Property<string>(r, "sections"))
            .Any(b => SpikeDbContext.JsonText(b.Value, "$.type") == blockType));

        Ids(query).ShouldBe([_heroDocument]);
        Sql(query.Select(r => EF.Property<Guid>(r, "document_id"))).ShouldBe("""
            .param set @blockType 'hero'

            SELECT "c"."document_id"
            FROM "cms_article" AS "c"
            WHERE EXISTS (
                SELECT 1
                FROM json_each("c"."sections") AS "j"
                WHERE json_extract("j"."value", '$.type') = @blockType)
            """);
    }

    [Fact]
    public void ScalarPathInsideABlockKeepsBothPredicatesOnTheSameBlock()
    {
        var blockType = "hero";
        var heading = "Welcome";
        var query = Articles.Where(r => _context.JsonEach(EF.Property<string>(r, "sections"))
            .Any(b => SpikeDbContext.JsonText(b.Value, "$.type") == blockType
                && SpikeDbContext.JsonText(b.Value, "$.heading") == heading));

        Ids(query).ShouldBe([_heroDocument]);
        Sql(query.Select(r => EF.Property<Guid>(r, "document_id"))).ShouldBe("""
            .param set @blockType 'hero'
            .param set @heading 'Welcome'

            SELECT "c"."document_id"
            FROM "cms_article" AS "c"
            WHERE EXISTS (
                SELECT 1
                FROM json_each("c"."sections") AS "j"
                WHERE json_extract("j"."value", '$.type') = @blockType AND json_extract("j"."value", '$.heading') = @heading)
            """);
    }

    [Fact]
    public void ScalarPathOnAJsonColumnTranslatesToJsonExtract()
    {
        var audience = "developers";
        var query = Articles.Where(r => SpikeDbContext.JsonText(EF.Property<string>(r, "metadata"), "$.audience") == audience);

        Ids(query).ShouldBe([_heroDocument]);
        Sql(query.Select(r => EF.Property<Guid>(r, "document_id"))).ShouldBe("""
            .param set @audience 'developers'

            SELECT "c"."document_id"
            FROM "cms_article" AS "c"
            WHERE json_extract("c"."metadata", '$.audience') = @audience
            """);
    }

    [Fact]
    public void NumericScalarPathComparesAsANumber()
    {
        var query = Articles.Where(r => SpikeDbContext.JsonInteger(EF.Property<string>(r, "metadata"), "$.minutes") >= 10);

        Ids(query).ShouldBe([_heroDocument]);
    }

    [Fact]
    public void TextComparisonOnANumericPathFindsNothingSoTheDialectMustBeTypeAware()
    {
        var query = Articles.Where(r => SpikeDbContext.JsonText(EF.Property<string>(r, "metadata"), "$.minutes") == "12");

        Ids(query).ShouldBeEmpty();
    }

    [Fact]
    public void StringContainmentOnAScalarPathTranslatesToInstr()
    {
        var term = "velop";
        var query = Articles.Where(r => SpikeDbContext.JsonText(EF.Property<string>(r, "metadata"), "$.audience")!.Contains(term));

        Ids(query).ShouldBe([_heroDocument]);
        Sql(query.Select(r => EF.Property<Guid>(r, "document_id"))).ShouldBe("""
            .param set @term 'velop'

            SELECT "c"."document_id"
            FROM "cms_article" AS "c"
            WHERE instr(json_extract("c"."metadata", '$.audience'), @term) > 0
            """);

        var upperTerm = "VELOP";
        Ids(Articles.Where(r => SpikeDbContext.JsonText(EF.Property<string>(r, "metadata"), "$.audience")!.Contains(upperTerm))).ShouldBeEmpty();
    }

    [Fact]
    public void ArrayContainmentAtAOneLevelPathTranslatesToJsonEach()
    {
        var label = "sql";
        var query = Articles.Where(r => _context.JsonEach(SpikeDbContext.JsonText(EF.Property<string>(r, "metadata"), "$.labels"))
            .Any(e => e.Value == label));

        Ids(query).ShouldBe([_heroDocument]);
        Sql(query.Select(r => EF.Property<Guid>(r, "document_id"))).ShouldBe("""
            .param set @label 'sql'

            SELECT "c"."document_id"
            FROM "cms_article" AS "c"
            WHERE EXISTS (
                SELECT 1
                FROM json_each(json_extract("c"."metadata", '$.labels')) AS "j"
                WHERE "j"."value" = @label)
            """);
    }

    [Fact]
    public void CompiledFilterAstBindsValuesAndInlinesTheState()
    {
        var filter = new AndFilter(
            new StateIs(1),
            new AndFilter(
                new ColumnEquals("category", "news"),
                new AndFilter(
                    new BlockFieldEquals("sections", "hero", "heading", "Welcome"),
                    new NotFilter(new JsonFieldEquals("metadata", "audience", "o'brien")))));

        var query = Articles.Where(FilterCompiler.Compile(_context, filter));

        Ids(query).ShouldBe([_heroDocument]);
        Sql(query.Select(r => EF.Property<Guid>(r, "document_id"))).ShouldBe("""
            .param set @Value 'news'
            .param set @Value1 'hero'
            .param set @Value2 'Welcome'
            .param set @Value3 'o''brien'

            SELECT "c"."document_id"
            FROM "cms_article" AS "c"
            WHERE "c"."state" = 1 AND "c"."category" = @Value AND EXISTS (
                SELECT 1
                FROM json_each("c"."sections") AS "j"
                WHERE json_extract("j"."value", '$.type') = @Value1 AND json_extract("j"."value", '$.heading') = @Value2) AND (json_extract("c"."metadata", '$.audience') <> @Value3 OR json_extract("c"."metadata", '$.audience') IS NULL)
            """);
    }

    [Fact]
    public void CompiledHasBlockFilterFindsOnlyMatchingRows()
    {
        var query = Articles.Where(FilterCompiler.Compile(_context, new HasBlock("sections", "quote")));

        Ids(query).ShouldBe([_heroDocument, _quoteDocument], ignoreOrder: true);
        Ids(query).ShouldNotContain(_emptyDocument);
    }

    [Fact]
    public void PathsDeeperThanOneLevelAreRejectedByTheCompiler()
    {
        Should.Throw<ArgumentException>(() => FilterCompiler.Compile(_context, new JsonFieldEquals("metadata", "seo.title", "x")));
    }

    [Fact]
    public void BlockFilterIsAFullScanOnSqlite()
    {
        var query = Articles.Where(FilterCompiler.Compile(_context, new HasBlock("sections", "hero")));

        SpikeDatabase.QueryPlan(query).ShouldContain("SCAN c");
    }

    [Fact]
    public void CheckConstraintRejectsMalformedJsonThatWouldOtherwiseBreakEveryFilter()
    {
        var insert = $"""
            insert into cms_article (id, document_id, state, site_id, category, sections)
            values ('{Guid.CreateVersion7()}', '{Guid.CreateVersion7()}', 1, '{SpikeSeed.SiteA}', 'news', 'not json')
            """;

        Should.Throw<SqliteException>(() => _db.Execute(insert)).SqliteExtendedErrorCode.ShouldBe(275);

        _db.Execute("pragma ignore_check_constraints = on");
        _db.Execute(insert);
        _db.Execute("pragma ignore_check_constraints = off");

        var query = Articles.Where(FilterCompiler.Compile(_context, new HasBlock("sections", "hero")));
        Should.Throw<SqliteException>(() => Ids(query)).Message.ShouldContain("JSON", Case.Insensitive);
    }
}
