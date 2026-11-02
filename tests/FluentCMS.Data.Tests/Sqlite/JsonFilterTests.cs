using System.Linq.Expressions;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Sqlite.RowFilter;

namespace FluentCMS.Data.Tests.Sqlite;

public sealed class JsonFilterTests : IDisposable
{
    private const string Post = "cms_post";

    private static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddBlock("hero", block => block.Field("heading", "text"))
        .AddBlock("quote", block => block.Field("text", "text"))
        .AddCollection("post", type => type
            .Field("category", "text")
            .Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, new[] { "hero", "quote" }))
            .Field("metadata", "json"))
        .Build();

    private readonly SqliteFileDatabase _database = new();
    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;
    private readonly CmsDbContext _context;
    private readonly IJsonSqlDialect _dialect;
    private readonly Guid _hero;
    private readonly Guid _quote;
    private readonly Guid _empty;

    public JsonFilterTests()
    {
        _services = _database.CreateServices(Schema);
        _scope = _services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        _dialect = _scope.ServiceProvider.GetRequiredService<IJsonSqlDialect>();
        _context.Database.EnsureCreated();

        _hero = Add(
            """[{"type":"hero","heading":"Welcome","level":2,"tags":["net"]},{"type":"quote","text":"Hi"}]""",
            """{"audience":"developers","minutes":12,"rating":4.5,"featured":true,"code":"12","labels":["net","sql"],"mixed":[12]}""");
        _quote = Add(
            """[{"type":"quote","text":"Welcome","heading":"Other"}]""",
            """{"audience":"o'brien","minutes":3,"rating":4,"featured":false,"flag":1,"labels":"net"}""");
        _empty = Add("[]", null);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
        _database.Dispose();
    }

    private Guid Add(string sections, string? metadata)
    {
        var id = Guid.CreateVersion7();
        _context.Rows(Post).Add(new Dictionary<string, object>
        {
            ["document_id"] = id,
            ["site_id"] = Guid.Empty,
            ["state"] = ContentState.Published,
            ["category"] = "news",
            ["sections"] = sections,
            ["metadata"] = metadata!,
        });

        return id;
    }

    private IQueryable<Dictionary<string, object>> Filter(Func<ParameterExpression, Expression> filter) =>
        _context.Rows(Post).Where(Where(filter));

    private List<Guid> Ids(Func<ParameterExpression, Expression> filter) =>
        [.. Filter(filter).Select(row => EF.Property<Guid>(row, "document_id"))];

    private static MethodCallExpression Sections(ParameterExpression row) => Column<string>(row, "sections");

    private static MethodCallExpression Metadata(ParameterExpression row) => Column<string>(row, "metadata");

    [Fact]
    public void HasBlockFindsARowHoldingABlockOfTheType()
    {
        Ids(row => _dialect.HasBlock(Sections(row), Bind("hero"))).ShouldBe([_hero]);
        Ids(row => _dialect.HasBlock(Sections(row), Bind("quote"))).ShouldBe([_hero, _quote], ignoreOrder: true);
        Ids(row => _dialect.HasBlock(Sections(row), Bind("video"))).ShouldBeEmpty();
    }

    [Fact]
    public void HasBlockTranslatesToExistsOverJsonEach()
    {
        var sql = Filter(row => _dialect.HasBlock(Sections(row), Bind("hero"))).ToQueryString();

        sql.ShouldContain("EXISTS");
        sql.ShouldContain("json_each(\"c\".\"sections\")");
        sql.ShouldContain("json_extract(\"j\".\"value\", '$.type') = @");
    }

    [Fact]
    public void AnyBlockAppliesEveryConditionToTheSameBlock()
    {
        Ids(row => _dialect.AnyBlock(Sections(row), Bind("hero"), block =>
            _dialect.ScalarEquals(block, "heading", JsonScalarType.Text, Bind("Welcome")))).ShouldBe([_hero]);

        // The hero document has a quote block and a heading, but no quote block with that heading.
        Ids(row => _dialect.AnyBlock(Sections(row), Bind("quote"), block =>
            _dialect.ScalarEquals(block, "heading", JsonScalarType.Text, Bind("Welcome")))).ShouldBeEmpty();
    }

    [Fact]
    public void AnyBlockComparesInsideABlockByType()
    {
        Ids(row => _dialect.AnyBlock(Sections(row), Bind("hero"), block =>
            _dialect.ScalarEquals(block, "level", JsonScalarType.WholeNumber, Bind(2L)))).ShouldBe([_hero]);
        Ids(row => _dialect.AnyBlock(Sections(row), Bind("hero"), block =>
            _dialect.ScalarEquals(block, "level", JsonScalarType.Text, Bind("2")))).ShouldBeEmpty();
    }

    [Fact]
    public void AnyBlockSupportsContainment()
    {
        Ids(row => _dialect.AnyBlock(Sections(row), Bind("quote"), block =>
            _dialect.ScalarContains(block, "text", Bind("elcom")))).ShouldBe([_quote]);
        Ids(row => _dialect.AnyBlock(Sections(row), Bind("hero"), block =>
            _dialect.ArrayContains(block, "tags", Bind("net")))).ShouldBe([_hero]);
    }

    [Fact]
    public void ScalarEqualsComparesText()
    {
        Ids(row => _dialect.ScalarEquals(Metadata(row), "audience", JsonScalarType.Text, Bind("developers"))).ShouldBe([_hero]);
        Ids(row => _dialect.ScalarEquals(Metadata(row), "audience", JsonScalarType.Text, Bind("o'brien"))).ShouldBe([_quote]);
        Ids(row => _dialect.ScalarEquals(Metadata(row), "audience", JsonScalarType.Text, Bind("DEVELOPERS"))).ShouldBeEmpty();
    }

    [Fact]
    public void ScalarEqualsComparesNumbers()
    {
        Ids(row => _dialect.ScalarEquals(Metadata(row), "minutes", JsonScalarType.WholeNumber, Bind(12L))).ShouldBe([_hero]);
        Ids(row => _dialect.ScalarEquals(Metadata(row), "rating", JsonScalarType.DecimalNumber, Bind(4.5m))).ShouldBe([_hero]);

        // A JSON integer and a decimal of the same value are equal.
        Ids(row => _dialect.ScalarEquals(Metadata(row), "rating", JsonScalarType.DecimalNumber, Bind(4m))).ShouldBe([_quote]);
    }

    [Fact]
    public void ScalarEqualsComparesBooleans()
    {
        Ids(row => _dialect.ScalarEquals(Metadata(row), "featured", JsonScalarType.Boolean, Bind(true))).ShouldBe([_hero]);
        Ids(row => _dialect.ScalarEquals(Metadata(row), "featured", JsonScalarType.Boolean, Bind(false))).ShouldBe([_quote]);
    }

    [Fact]
    public void AComparisonIsSpecificToTheJsonType()
    {
        // minutes is the number 12, code is the text "12", flag is the number 1.
        Ids(row => _dialect.ScalarEquals(Metadata(row), "minutes", JsonScalarType.Text, Bind("12"))).ShouldBeEmpty();
        Ids(row => _dialect.ScalarEquals(Metadata(row), "code", JsonScalarType.Text, Bind("12"))).ShouldBe([_hero]);
        Ids(row => _dialect.ScalarEquals(Metadata(row), "code", JsonScalarType.WholeNumber, Bind(12L))).ShouldBeEmpty();
        Ids(row => _dialect.ScalarEquals(Metadata(row), "code", JsonScalarType.DecimalNumber, Bind(12m))).ShouldBeEmpty();
        Ids(row => _dialect.ScalarEquals(Metadata(row), "flag", JsonScalarType.Boolean, Bind(true))).ShouldBeEmpty();
    }

    private Guid AddShapes()
    {
        var id = Add("[]", """{"flag":true,"count":1,"name":"x","tags":["x"],"nested":[["x"]],"data":{"a":1}}""");
        _context.SaveChanges();
        return id;
    }

    [Fact]
    public void ABooleanDoesNotEqualAWholeNumber()
    {
        AddShapes();

        // The quote document holds the number 1 at flag, which still matches.
        Ids(row => _dialect.ScalarEquals(Metadata(row), "flag", JsonScalarType.WholeNumber, Bind(1L))).ShouldBe([_quote]);
    }

    [Fact]
    public void ABooleanDoesNotEqualADecimalNumber()
    {
        AddShapes();

        Ids(row => _dialect.ScalarEquals(Metadata(row), "flag", JsonScalarType.DecimalNumber, Bind(1.0m))).ShouldBe([_quote]);
    }

    [Fact]
    public void AnArrayDoesNotEqualItsJsonText()
    {
        AddShapes();

        Ids(row => _dialect.ScalarEquals(Metadata(row), "tags", JsonScalarType.Text, Bind("""["x"]"""))).ShouldBeEmpty();
    }

    [Fact]
    public void AnObjectDoesNotEqualItsJsonText()
    {
        AddShapes();

        Ids(row => _dialect.ScalarEquals(Metadata(row), "data", JsonScalarType.Text, Bind("""{"a":1}"""))).ShouldBeEmpty();
    }

    [Fact]
    public void ANestedArrayIsNotAnElementOfText()
    {
        AddShapes();

        Ids(row => _dialect.ArrayContains(Metadata(row), "nested", Bind("""["x"]"""))).ShouldBeEmpty();
    }

    [Fact]
    public void OrdinaryValuesStillMatchNextToTheOtherShapes()
    {
        var shapes = AddShapes();

        Ids(row => _dialect.ScalarEquals(Metadata(row), "count", JsonScalarType.WholeNumber, Bind(1L))).ShouldBe([shapes]);
        Ids(row => _dialect.ScalarEquals(Metadata(row), "count", JsonScalarType.DecimalNumber, Bind(1.0m))).ShouldBe([shapes]);
        Ids(row => _dialect.ScalarEquals(Metadata(row), "name", JsonScalarType.Text, Bind("x"))).ShouldBe([shapes]);
        Ids(row => _dialect.ArrayContains(Metadata(row), "tags", Bind("x"))).ShouldBe([shapes]);
    }

    [Fact]
    public void AWholeNumberEqualsARealOfTheSameValue()
    {
        var real = Add("[]", """{"count":12.0}""");
        _context.SaveChanges();

        Ids(row => _dialect.ScalarEquals(Metadata(row), "count", JsonScalarType.WholeNumber, Bind(12L))).ShouldBe([real]);
    }

    [Fact]
    public void ScalarContainsMatchesInsideTextCaseSensitively()
    {
        Ids(row => _dialect.ScalarContains(Metadata(row), "audience", Bind("velop"))).ShouldBe([_hero]);
        Ids(row => _dialect.ScalarContains(Metadata(row), "audience", Bind("VELOP"))).ShouldBeEmpty();
    }

    [Fact]
    public void ScalarContainsLooksOnlyAtText()
    {
        Ids(row => _dialect.ScalarContains(Metadata(row), "minutes", Bind("1"))).ShouldBeEmpty();
        Ids(row => _dialect.ScalarContains(Metadata(row), "mixed", Bind("1"))).ShouldBeEmpty();
    }

    [Fact]
    public void ArrayContainsMatchesAWholeElement()
    {
        Ids(row => _dialect.ArrayContains(Metadata(row), "labels", Bind("sql"))).ShouldBe([_hero]);
        Ids(row => _dialect.ArrayContains(Metadata(row), "labels", Bind("sq"))).ShouldBeEmpty();
    }

    [Fact]
    public void ListContainsMatchesAWholeTextElementOfTheColumn()
    {
        var list = Add("""["net",12]""", null);
        _context.SaveChanges();

        Ids(row => _dialect.ListContains(Sections(row), Bind("net"))).ShouldBe([list]);
        Ids(row => _dialect.ListContains(Sections(row), Bind("ne"))).ShouldBeEmpty();
        Ids(row => _dialect.ListContains(Sections(row), Bind("12"))).ShouldBeEmpty();
    }

    [Fact]
    public void ArrayContainsLooksOnlyAtArraysOfText()
    {
        // In the quote document labels is the text "net", and mixed holds the number 12.
        Ids(row => _dialect.ArrayContains(Metadata(row), "labels", Bind("net"))).ShouldBe([_hero]);
        Ids(row => _dialect.ArrayContains(Metadata(row), "mixed", Bind("12"))).ShouldBeEmpty();
        Ids(row => _dialect.ArrayContains(Metadata(row), "absent", Bind("net"))).ShouldBeEmpty();
    }

    [Fact]
    public void AFilterNegatesIncludingRowsWithoutThePath()
    {
        Ids(row => Expression.Not(_dialect.ScalarEquals(Metadata(row), "audience", JsonScalarType.Text, Bind("developers"))))
            .ShouldBe([_quote, _empty], ignoreOrder: true);
    }

    [Fact]
    public void AFilterComposesWithOrdinaryColumnsAndAnInlinedState()
    {
        var query = Filter(row => Expression.AndAlso(
            Expression.AndAlso(
                Expression.Equal(Column<short>(row, "state"), Expression.Constant(ContentState.Published)),
                Expression.Equal(Column<string>(row, "category"), Bind("news"))),
            _dialect.HasBlock(Sections(row), Bind("hero"))));

        query.Select(row => EF.Property<Guid>(row, "document_id")).ToList().ShouldBe([_hero]);
        query.ToQueryString().ShouldContain("\"c\".\"state\" = 1");
    }

    [Fact]
    public void UserValuesAreBoundAsParameters()
    {
        var sql = Filter(row => _dialect.AnyBlock(Sections(row), Bind("hero"), block =>
            _dialect.ScalarEquals(block, "heading", JsonScalarType.Text, Bind("Welcome")))).ToQueryString();

        sql.ShouldContain(".param set");
        sql.ShouldNotContain("= 'Welcome'");
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("a[0]")]
    [InlineData("x') OR ('1'='1")]
    [InlineData("")]
    public void APathDeeperThanOneLevelOrNotAPlainNameIsRejected(string field)
    {
        Should.Throw<ArgumentException>(() => _dialect.ScalarEquals(Expression.Constant("{}"), field, JsonScalarType.Text, Bind("x")));
        Should.Throw<ArgumentException>(() => _dialect.ScalarContains(Expression.Constant("{}"), field, Bind("x")));
        Should.Throw<ArgumentException>(() => _dialect.ArrayContains(Expression.Constant("{}"), field, Bind("x")));
    }

    [Fact]
    public void AValueOfTheWrongClrTypeIsRejected() =>
        Should.Throw<ArgumentException>(() => _dialect.ScalarEquals(Expression.Constant("{}"), "minutes", JsonScalarType.WholeNumber, Bind("12")));

    [Theory]
    [InlineData("text", JsonScalarType.Text)]
    [InlineData("longText", JsonScalarType.Text)]
    [InlineData("email", JsonScalarType.Text)]
    [InlineData("url", JsonScalarType.Text)]
    [InlineData("slug", JsonScalarType.Text)]
    [InlineData("select", JsonScalarType.Text)]
    [InlineData("date", JsonScalarType.Text)]
    [InlineData("dateTime", JsonScalarType.Text)]
    [InlineData("integer", JsonScalarType.WholeNumber)]
    [InlineData("decimal", JsonScalarType.DecimalNumber)]
    [InlineData("boolean", JsonScalarType.Boolean)]
    [InlineData("json", null)]
    [InlineData("blocks", null)]
    [InlineData("richText", null)]
    [InlineData("relation", null)]
    public void EachFieldTypeHasOneComparison(string fieldType, JsonScalarType? expected) =>
        JsonScalar.TypeOf(fieldType).ShouldBe(expected);
}
